# Crouching NPCs in Medieval Engineers

This note documents the reliable way to make a custom Medieval Engineers NPC crouch through the character movement and animation systems.

The behavior was verified against the local game assemblies, especially `Sandbox.Game.dll`, and against the `SiUtilityAI` implementation in `mod/Data/Scripts/CombatAI/SiGroundedNpc.cs`.

## Short version

NPC crouching is a movement-state request that the vanilla character animation controller consumes. It is not an animation action that a mod should start manually.

The reliable pipeline is:

1. Store a crouch intent in the NPC controller.
2. Call `MyCharacterMovementComponent.TryCrouch(intent)` immediately when the intent changes.
3. Call `TryCrouch(intent)` again from `MovementIndicatorHandler`, before the movement component calculates its next movement state.
4. Let `CharacterAnimationControllerComponent` read the committed movement state and drive the vanilla crouch animation.
5. Keep the intent true while the NPC is holding cover, and clear it only when a new movement path starts or the combat posture is explicitly abandoned.

## Why the first implementation did not work

`TryCrouch(true)` does not directly switch the skeleton to a crouch animation. The method updates the movement component's desired modifiers. The movement component later commits those modifiers into its movement state, and the animation controller reads `IsCrouching` from that state.

Calling `TryCrouch` from `OnPostProcessPhysicalMovement` was too late for the movement-state calculation in that update. The request could be accepted while the current animation state remained standing. The correct timing is the movement indicator callback, before movement-state calculation.

There was also a behavior-layer problem. Cover and plain-view behaviors are non-continuous utility behaviors: once their destination is reached, they can stop scoring and receive `End()` instead of another movement tick. The final crouch request therefore has to be applied during the arrival handoff as well.

Finally, every utility behavior is evaluated periodically, including behaviors that are not active. An inactive behavior must not call `TrySetCrouch(false)` from a generic reset path, because it can clear the active cover behavior's crouch request on the next decision interval. Reset methods should clear their own cached movement state; standing should be requested only by the behavior that owns a new movement path or an explicit posture transition.

## Required character definition

The character entity must have the normal movement and animation components. The relevant part of an NPC container definition is:

```xml
<Component Type="CharacterMovementComponent" Subtype="SiTrooperMovement" />
<Component Type="SkeletonComponent" Subtype="Medieval_male" />
<Component Type="AnimationCollisionComponent" Subtype="Medieval_male" />
<Component Type="CharacterAnimationControllerComponent" Subtype="Medieval_male" />
```

The movement definition must be compatible with the humanoid skeleton and must expose the desired movement tuning, including crouch speed where appropriate. Do not replace the character animation controller with an ad-hoc animation component.

## Grounded NPC controller pattern

Keep the intent in the grounded controller and apply it at both useful points:

```csharp
private bool _wantsCrouch;

public void SetCrouch(bool wantsCrouch)
{
    _wantsCrouch = wantsCrouch;
    _movement?.TryCrouch(wantsCrouch);
}

private void MovementIndicatorHandler(
    MyCharacterMovementComponent movement,
    ref Vector3 moveIndicator)
{
    movement.TryCrouch(_wantsCrouch);

    if (!TryGetMoveDirection(out var direction, GetControllerDefinition()))
    {
        moveIndicator = Vector3.Zero;
        return;
    }

    var localDirection = Vector3D.TransformNormal(
        direction,
        Entity.PositionComp.WorldMatrixNormalizedInv);
    moveIndicator = (Vector3)localDirection;
    if (_wantsCrouch)
        moveIndicator.Y = -1f;
}
```

The immediate call in `SetCrouch` covers requests made after the current movement callback. The callback call reasserts the intent at the exact point where the movement component consumes it, including while the NPC is stationary.

Do not set the animation variable named `crouch` manually. The vanilla `CharacterAnimationControllerComponent` owns that variable and derives it from the movement component's committed `IsCrouching` state.

## Cover and plain-view behavior pattern

The posture decision should be based on arrival at the actual reserved position, not on whether the behavior was selected:

```csharp
// Regular cover
context.TrySetCrouch(!IsRunningToCover(context));

// Plain view
if (HasReachedDestination(context))
    context.TrySetCrouch(true);
else
    context.TrySetCrouch(false);
```

For a non-continuous behavior, repeat the hold request in `End()` when the behavior is ending because the destination has been reached:

```csharp
void ISiUtilityBehavior.End(SiUtilityContext context)
{
    if (_hasReservedCover && !IsRunningToCover(context))
        context.TrySetCrouch(true);
}
```

The same rule applies to plain view. This closes the gap where the last movement tick leaves the destination but the next utility decision immediately ends the movement behavior.

## Preventing delayed stand-up

A reset method can be called by an inactive behavior during utility evaluation. These methods must not blindly clear crouch:

```csharp
private void ResetState(...)
{
    ClearCachedMovementState();
    // Do not call context.TrySetCrouch(false) here when this behavior may be inactive.
}
```

Standing requests belong in the movement branches that actually start a new path:

```csharp
context.TrySetCrouch(false);
context.TrySetWaypoint(nextMovementTarget);
```

This makes crouch persist while the NPC holds cover or plain view, while still allowing the next repositioning movement to stand the NPC up.

## Debugging checklist

When crouching fails, inspect the stages in this order:

- Confirm the entity has `MyCharacterMovementComponent` and `MyCharacterAnimationControllerComponent`.
- Confirm `SetCrouch(true)` is reached by the active behavior.
- Confirm `TryCrouch(true)` is called from `MovementIndicatorHandler`, not only post-process movement.
- Check `MyCharacterMovementComponent.IsCrouching` after movement-state processing. `TryCrouch` returning `true` only means the request was accepted.
- Check whether another behavior calls `TrySetCrouch(false)` on a later decision tick.
- Check whether a non-continuous cover/plain-view behavior needs an arrival handoff in `End()`.

Temporary probes should use `SiCore.Core.Debug.SiGameLog`, remain disabled by default, and end each added log line with `// AGENT-DEBUG-LOG`. Remove them after the movement state and animation pipeline are confirmed.

## Relevant SiUtilityAI files

- `mod/Data/Scripts/CombatAI/SiGroundedNpc.cs` — movement callback and crouch intent bridge
- `mod/Data/Scripts/UtilityAI/Behaviors/SiTakeCoverBehavior.cs` — regular cover arrival and hold posture
- `mod/Data/Scripts/UtilityAI/Behaviors/SiTakePlainViewBehavior.cs` — plain-view arrival and hold posture
- `mod/Data/Scripts/UtilityAI/Behaviors/SiAdvanceTowardEnemyLeaderBehavior.cs` — leader cover movement
- `mod/Data/Bots/SiNpcShared.sbc` — character component wiring
- `ref_vanilla_content/Data/Characters/Engineer_Male/AnimationController.sbc` — vanilla crouch animation state machine
