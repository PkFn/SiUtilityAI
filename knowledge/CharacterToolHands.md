# Character Tool Hands in Medieval Engineers

This note describes how the game API handles the "tool hands" system: the pieces that make an item appear in a character's hands and give it live behavior.

I examined:

- Repo usage in `SiUtilityAI`, especially `MyCharacterHandItemsComponent` access in `mod/Data/Scripts/UtilityAI/Spotting/SiSpottingSystem.cs` and equipment forcing in `mod/Data/Scripts/CombatAI/SiNpcRangedWeaponComponent.cs`
- Vanilla content definitions under `ref_vanilla_content/Data/Characters` and `ref_vanilla_content/Data/Items/Equipment`
- Workshop hand-item examples such as `ref_workshop/1088868028/Data/Matchlock.sbc`
- Local dependency code in `ref_pax_core`, including custom hand item serialization and custom hand-item behaviors
- Game assemblies from `F:\SteamLibrary\steamapps\common\MedievalEngineers\Bin64`, mainly `Sandbox.Game.dll`, `MedievalEngineers.Game.dll`, and `VRage.Game.dll`

## Short version

The item you see in a character's hands is not a special one-off visual. It is a normal equipped inventory item of type `MyHandItem`, managed by the character's equipment component and wrapped by `MyCharacterHandItemsComponent`, which creates and drives one or more `MyHandItemBehaviorBase` behaviors for the active hand item.

In practice the pipeline is:

1. A `HandItem` definition declares model, slots, transforms, and behavior definitions in `.sbc`.
2. The humanoid character has an `EntityEquipmentComponent`, `ModelAttachmentComponent`, and `CharacterHandItemsComponent`.
3. When a `MyHandItem` is equipped, the equipment system creates the visible item entity and attaches it to hand-related attachment slots/bones.
4. `MyCharacterHandItemsComponent` tracks the equipped hand item as `MainHand` / `OffHand`, instantiates its hand-item behaviors, and routes primary/secondary/tertiary actions to them.
5. The active behavior can then drive gameplay, animation, targeting, projectiles, lights, extra subparts, and so on.

## Main runtime types

These types are confirmed from the loaded assemblies.

- `Sandbox.Game.Inventory.MyHandItem`
  - Base: `MyEquipmentItem`
  - This is the inventory item class for a hand-held tool/weapon.
  - It exposes `GetDefinition()` returning `MyHandItemDefinition`.

- `Sandbox.Definitions.Inventory.MyHandItemDefinition`
  - Base: `MyEquipmentItemDefinition`
  - Adds hand-item-specific behavior definitions and `AllowEmotes`.
  - In the object builder shape, this is authored as `VRage.ObjectBuilders.Definitions.Inventory.MyObjectBuilder_HandItemDefinition`.

- `Sandbox.Game.EntityComponents.Character.MyCharacterHandItemsComponent`
  - Exposes `MainHand`, `OffHand`, `ActiveStance`, `GetBehavior<T>()`, `FireAction(...)`, `HandleActionInput(...)`, and `UpdateHand(...)`.
  - This is the character-side controller that bridges equipped items to live hand-item behaviors.

- `Sandbox.Game.EntityComponents.Character.MyHandItemBehaviorBase`
  - Base class for live tool behaviors.
  - Holds `Holder`, `Item`, `Definition`, `Equipment`, `HandItemsComponent`, targeting/animation/stat helpers, and methods like `Activate()`, `Deactivate()`, `StartAction(...)`, `EndAction(...)`, `SetSecondary(...)`, `SetTarget()`, and `GetItemEntity()`.

- `Sandbox.Definitions.Equipment.MyHandItemBehaviorDefinition`
  - Definition base for hand-item behaviors.
  - Custom behavior definitions derive from this.

- `Medieval.GameSystems.Tools.MyRangedWeaponBehavior`
  - Base: `MyHandItemBehaviorBase`
  - Adds ranged-weapon logic such as reload, shoot, ironsight, ammo lookup, and projectile spawn helpers.

## Character setup: where the hands come from

Vanilla humanoids wire the hand system with three components:

- `ModelAttachmentComponent`
- `EntityEquipmentComponent`
- `CharacterHandItemsComponent`

You can see those on the humanoid container in `ref_vanilla_content/Data/Characters/Entities.sbc`.

The attachment names used for tools are defined in `ref_vanilla_content/Data/Characters/CommonComponents.sbc`:

- `GhostHand -> WeaponDummy`
- `OffHand -> ME_RigL_Weapon_pin`
- `MainHand -> ME_RigR_Weapon_pin`

The equipment slots that use those attachments are defined in `ref_vanilla_content/Data/Characters/HumanoidEquipment.sbc`:

- `GhostHand`
- `OffHand`
- `MainHand`

Those slot definitions also carry equip/unequip animation event names like `equip_right_tool` and `unequip_right_tool`.

The separate `CharacterHandItemsComponent` definition in `CommonComponents.sbc` names `MainHand` as the main slot. That means the visible hand item is built on top of the general equipment-slot system rather than bypassing it.

## Hand item data: what `.sbc` authors define

A tool/weapon shown in hand is authored as `MyObjectBuilder_HandItemDefinition`.

Confirmed object-builder fields:

- `Behaviors`
- `StanceToBehaviors`
- `AllowEmotes`

Because `MyObjectBuilder_HandItemDefinition.Position` comes from `MyObjectBuilder_EquipmentItemDefinition`, a hand item also inherits the normal equipment-item data:

- model
- bearer definition
- slot positions
- transforms
- dummy mappings
- animation id

### Common authored fields

From vanilla and workshop examples, a hand item commonly defines:

- `<Model>`: the visible model
- `<BearerDefinition Type="MyObjectBuilder_EntityEquipmentComponent" Subtype="Humanoid" />`
- one or more `<Position>` entries with `<Slot>` children such as `MainHand`, `OffHand`, or `GhostHand`
- optional `<EquippedTransform>`, `<EquippedTransformFps>`, and `<EquippedTransformFpsCrouch>`
- optional `<DummyMapping>` entries for animation variables / model dummies
- `<Behavior>` entries and/or `<StanceToBehavior>` entries

### Example patterns

Vanilla hammer tools use ordered generic behaviors:

- `PhysicalPowerToolBehaviorDefinition`
- `BuilderToolBehaviorDefinition`
- `MeleeWeaponBehaviorDefinition`

See `ref_vanilla_content/Data/Items/Equipment/Wooden/HammerWood.sbc`.

Vanilla and workshop weapons often use stance-specific behavior mapping, for example:

- `NormalMode`
- `CombatMode`

See `ref_vanilla_content/Data/Items/Equipment/Wooden/ShovelWood.sbc` and `ref_workshop/1088868028/Data/Matchlock.sbc`.

## Behavior selection and stance

There are two authoring styles visible in the API/data:

- ordered `<Behavior>` entries
- stance-keyed `<StanceToBehavior>` entries

What is confirmed:

- `MyObjectBuilder_HandItemDefinition` has both `Behaviors` and `StanceToBehaviors`
- `MyCharacterHandItemsComponent` exposes `ActiveStance`
- vanilla character states include `NormalMode` and `CombatMode`

So the intended model is clearly that hand items can expose behaviors either generally or by character stance. I did not decompile `UpdateHand(...)`, so the exact selection order inside the engine is an inference from the API shape and content usage rather than a directly recovered method body.

## How the visible in-hand entity is accessed

`MyHandItemBehaviorBase` exposes `GetItemEntity()`.

That is important: behaviors do not only operate on abstract inventory data. They can reach the actual equipped item entity that is attached to the character.

Examples from local dependencies:

- `ref_pax_core/Data/Scripts/MiscTools/MyHandheldFlashlightBehavior.cs`
  - The behavior fetches the equipped item entity with `GetItemEntity()`
  - Then it grabs a light component from that entity and updates it while equipped

- `ref_pax_core/Data/Scripts/CannonGunsExplosives/MyGunBehavior.cs`
  - Repeatedly accesses `GetItemEntity()` to drive gun visuals/effects

This is the core answer to "ones used for showing items in hands": the shown object is an entity created by the equipment system for the equipped `MyHandItem`, and behaviors can modify that entity while it is attached.

## How equip state reaches gameplay code

`SiUtilityAI` itself uses the hand-item component at the behavior level, not by manually handling model attachments.

Examples:

- `mod/Data/Scripts/UtilityAI/Spotting/SiSpottingSystem.cs`
  - Reads `MyCharacterHandItemsComponent`
  - Calls `GetBehavior<MyHandItemBehaviorBase>()`
  - Checks `MainHand` and `behavior.IsActive`

- `mod/Data/Scripts/CombatAI/SiNpcRangedWeaponComponent.cs`
  - Forces a held weapon to equip through the NPC equipment helper
  - Then fires via the PAX gun behavior/network path

That matches the game architecture: mods usually work through equip state and behaviors, not by hand-spawning a mesh into a hand bone.

## Per-instance state on held items

If a mod needs runtime state stored on the specific held item instance, it can extend the inventory item/object builder pair.

`ref_pax_core/Data/Scripts/FluidsAndIndustrial/HandItemWithVariable.cs` shows this pattern:

- `MyObjectBuilder_HandItemWithVariable : MyObjectBuilder_HandItem`
- `MyObjectBuilder_HandItemWithVariableDefinition : MyObjectBuilder_HandItemDefinition`
- `MyHandItemWithVariable : MyHandItem`

That custom item stores extra fields like:

- `Content`
- `MixRatio`

and persists them through `Serialize()` / `Deserialize()`.

So "hand item" is not only a visual definition; it is also the concrete inventory item class you can extend for per-item state.

## Practical modding takeaways

- To make something appear in a character's hands, define it as a `HandItem`, not just as an arbitrary entity model.
- The visible hand object is anchored through equipment slots and attachment bones, not directly through ad hoc render code.
- The live logic belongs in a `MyHandItemBehaviorBase` subclass or one of its descendants like `MyRangedWeaponBehavior`.
- If you need extra behavior-specific visuals, use `GetItemEntity()` from the hand-item behavior and attach components/subparts there.
- If you need runtime item state, extend `MyObjectBuilder_HandItem` / `MyHandItem`.
- For NPCs, equipping the right `HandItem` is the important step; once equipped, the character hand-item component exposes the active behavior to other systems.

## One subtle point for SiUtilityAI

`mod/Data/Scripts/CombatAI/SiNpcCharacterDamageBridge.cs` contains an important comment: the mod currently avoids one vanilla death path because tearing down the equipped visual hand item during death can crash.

That suggests the hand-item visual entity lifecycle is tightly coupled to the component teardown path. So if you ever patch NPC death/equip transitions again, treat `MyCharacterHandItemsComponent` removal and equipped item teardown as a sensitive area.

## Best mental model

The cleanest mental model is:

- `MyHandItemDefinition` says what the held thing is
- `MyEntityEquipmentComponent` makes it equipped and visible on character attachment slots
- `MyCharacterHandItemsComponent` makes it behave like a live hand tool
- `MyHandItemBehaviorBase` (or derived classes) runs the actual tool logic on top of the equipped item entity

That is the hand-tool stack the game uses for "items shown in hands".
