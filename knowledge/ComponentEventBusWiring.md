# Wiring `MyComponentEventBus` listeners

This note documents the reliable pattern for listening to Medieval Engineers component events, with `PAX_ShootingDefender` `StartShoot` tracking as the concrete example.

The local references used for this note are:

- `ref_equi_core/Data/Scripts/Core/Modifiers/Extra/EquiEventModifierComponent.cs`
- `ref_equi_core/Data/Scripts/Core/Inventory/EquiInvertedVisualInventoryComponent.cs`
- `ref_pax_core/Data/Scripts/AI scripts/ShootingDefender.cs`
- the local `SiUtilityAI` implementation in `mod/Data/Scripts/CombatAI/SiStaticDefenderSystem.cs`

## Short version

`MyComponentEventBus` listeners are keyed by string event names, and the callback signature is `Action<string>`, not `Action<bool>`.

The safe pattern is:

1. Get a stable reference to the entity's `MyComponentEventBus`.
2. Register listeners only after the entity and its components are actually available.
3. Store the exact delegate instance you add.
4. Remove that same delegate when the entity leaves the scene or your watcher stops tracking it.
5. Route the event into your existing gameplay path instead of creating a parallel behavior path when possible.

For shooting defenders, the useful event is `StartShoot`. In `SiUtilityAI`, the listener does not set spotting directly. It calls the existing `SiSpottingSystem.ReportShot(...)` path so the normal spotting definition values still control shot awareness.

## Verified API shape

Reflection against the local game assemblies shows these public instance methods on `VRage.Game.Components.MyComponentEventBus`:

```csharp
bool AddListener(string eventId, Action<string> listener);
bool TryAddListener(string eventId, Action<string> listener);
void RemoveListener(string eventId, Action<string> listener);
void Invoke(string eventId, bool replicateIfServer);
```

Two details matter:

- The listener callback receives the event name as `string`.
- `RemoveListener` must be given the same delegate instance that was added.

## Pattern 1: component owns its own wiring

Use this when the listener lives on the same entity component that declares a normal dependency on `MyComponentEventBus`.

```csharp
[MyDependency(typeof(MyComponentEventBus), Critical = false)]
public class ExampleComponent : MyEntityComponent
{
    [Automatic]
    private readonly MyComponentEventBus _eventBus = null;

    public override void OnAddedToScene()
    {
        base.OnAddedToScene();
        _eventBus?.AddListener("SomeEvent", HandleEvent);
    }

    public override void OnRemovedFromScene()
    {
        _eventBus?.RemoveListener("SomeEvent", HandleEvent);
        base.OnRemovedFromScene();
    }

    private void HandleEvent(string eventName)
    {
    }
}
```

This is the pattern used throughout `ref_equi_core`.

## Pattern 2: external watcher tracks another entity

Use this when a session system or manager is watching foreign runtime components and cannot rely on `[Automatic]` field injection.

This is the pattern used for defender shot tracking in `SiStaticDefenderSystem`:

```csharp
private const string ShootEventId = "StartShoot";

private void Subscribe(TrackedTarget target)
{
    if (target == null || target.ShootListener != null)
        return;

    var eventBus = target.Entity?.Components?.Get<MyComponentEventBus>();
    if (eventBus == null)
        return;

    Action<string> listener = eventId => OnTargetShot(target);
    if (!eventBus.TryAddListener(ShootEventId, listener))
        return;

    target.EventBus = eventBus;
    target.ShootListener = listener;
}

private static void Unsubscribe(TrackedTarget target)
{
    if (target?.EventBus == null || target.ShootListener == null)
        return;

    target.EventBus.RemoveListener(ShootEventId, target.ShootListener);
    target.EventBus = null;
    target.ShootListener = null;
}
```

The key rule here is that the lambda is stored on the tracked record. Creating a new lambda during removal will not unregister the original listener.

## `StartShoot` example

`PAX_ShootingDefender` invokes:

```csharp
m_eventBus.Invoke("StartShoot");
```

when it actually fires. `SiUtilityAI` listens to that event and forwards it into the spotting system:

```csharp
private void OnDefenderShot(SiStaticDefenderTarget target)
{
    var entity = target?.Entity;
    if (entity == null || target.IsKnockedOut)
        return;

    _session?.Spotting?.ReportShot(entity.EntityId, entity);
}
```

That is the preferred integration point because:

- the event only fires when the defender really shoots
- the target is revealed through existing spotting logic
- spotting constants from `SiSpottingSystemDefinition` still apply
- NPC weapons and defender weapons share one shot-awareness pipeline

## When to use `AddListener` vs `TryAddListener`

Prefer `TryAddListener` when a watcher may refresh or revisit the same entity repeatedly, such as a session tracker.

Prefer `AddListener` when duplicate registration is already structurally impossible and a failure should be considered abnormal.

In either case, keep an explicit `ShootListener != null` or equivalent guard so repeated update passes do not attempt to subscribe again.

## Cleanup rules

Always unsubscribe when any of these happen:

- the watched entity leaves the scene
- your tracking record is removed
- your session/component unloads
- the listener should stop applying because the feature is disabled

For session-owned trackers, it is usually easiest to:

1. remove listeners from stale tracked records during the update pass
2. remove listeners from every remaining record in `Clear()` or `OnUnload()`

## Common mistakes

- Using `Action<bool>` because `Invoke` takes a `bool replicateIfServer` parameter. The listener does not receive that bool.
- Removing with a different lambda instance.
- Registering before the watched entity has a `MyComponentEventBus`.
- Forgetting that event names are plain strings and must match the producer exactly.
- Building a second gameplay path instead of forwarding the event into the already-authoritative system.

## Recommendation for future integrations

When another mod or vanilla component already emits a useful event:

1. confirm the exact event name in source or decompiled code
2. confirm the owning entity actually has `MyComponentEventBus`
3. subscribe with a stored `Action<string>`
4. feed the event into the existing Si system that already owns the gameplay meaning

For combat integrations, prefer forwarding to `SiSpottingSystem.ReportShot(...)`, `ReportNpcShotAt(...)`, or another existing combat-state entry point instead of inventing new direct state mutations.
