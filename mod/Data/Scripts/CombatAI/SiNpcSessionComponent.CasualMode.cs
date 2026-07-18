using Equinox76561198048419394.Core.Inventory;
using Sandbox.Game.Entities;
using Sandbox.Game.Inventory;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Components.Physics;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        internal void HandleNpcKilled(SiNpc npc)
        {
            if (!_casualModeEnabled)
                return;

            DropCasualNpcWebbing(npc);
        }

        internal void HandleNpcClosing(SiNpc npc)
        {
            if (!_casualModeEnabled)
                return;

            DropCasualNpcWebbing(npc);
        }

        internal static bool DropCasualNpcWebbing(SiNpc npc)
        {
            if (npc == null || npc.CasualLootHandled)
                return false;

            npc.CasualLootHandled = true;
            var entity = npc.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            var dataDrivenNpc = npc as SiDataDrivenNpc;
            if (dataDrivenNpc == null
                || string.IsNullOrWhiteSpace(dataDrivenNpc.WebbingSubtype)
                || !SiNpcTrooperCatalog.TryResolveLoadout(
                    dataDrivenNpc.WebbingSubtype,
                    false,
                    out _,
                    out var loadout)
                || loadout == null)
                return false;

            // Do not remove live inventory entries here.  The NPC's weapons and
            // webbing are still owned by the equipment/hand-item components;
            // mutating that inventory before Entity.Close invalidates their
            // item handles and can take down the game.  Closing the custom NPC
            // disposes its inventory without creating a normal loot drop.
            var webbing = MyInventoryItem.Create(loadout.WebbingItemId, 1);
            if (webbing == null)
                return false;

            var dropped = InventoryDropper.DropItem(
                webbing,
                entity.WorldMatrix.Translation,
                entity.Get<MyPhysicsComponentBase>());
            return dropped != null;
        }
    }
}
