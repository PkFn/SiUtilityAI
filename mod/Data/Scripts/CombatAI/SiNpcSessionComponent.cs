using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Session;
using VRage.Entities.Gravity;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Network;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Si.UtilityAI
{
    [StaticEventOwner]
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    public sealed class SiNpcSessionComponent : MySessionComponent
    {
        private const string Command = "/si-npc";
        private const double SpawnDistance = 2.5;

        private static SiNpcSessionComponent _instance;

        [Automatic]
        private readonly MyChatSystem _chat = null;

        public SiNpcManager Npcs { get; private set; }

        protected override void OnLoad()
        {
            base.OnLoad();
            _instance = this;
            Npcs = new SiNpcManager();

            _chat?.RegisterChatCommand(
                Command,
                HandleCommand,
                "Manage custom Si Utility AI NPCs. /si-npc spawn [soldier-dummy] | list | clear",
                MyChatCommandType.Server);
        }

        protected override void OnSessionReady()
        {
            base.OnSessionReady();
            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => RequestNpcSnapshot);
        }

        protected override void OnUnload()
        {
            Npcs?.CloseAll();
            Npcs = null;
            if (_instance == this)
                _instance = null;
            base.OnUnload();
        }

        [Update(100)]
        private void UpdateNpcs(long elapsedMilliseconds)
        {
            Npcs?.Update(elapsedMilliseconds);
        }

        private bool HandleCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            if (!MyAPIGateway.Session.CreativeMode && !MyAPIGateway.Session.IsAdminModeEnabled(sender))
                return Respond(sender, "Enable Medieval Master to manage custom NPCs in survival.");

            var tokens = message.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                return Respond(sender, HelpText());

            switch (tokens[1].ToLowerInvariant())
            {
                case "spawn":
                    return SpawnFromCommand(sender, tokens.Length >= 3
                        ? tokens[2]
                        : SiNpcManager.SoldierDummyArchetype);
                case "list":
                    return Respond(sender, $"Custom NPCs alive: {Npcs.Npcs.Count}.");
                case "clear":
                    var removed = Npcs.Npcs.Count;
                    Npcs.CloseAll();
                    BroadcastClear();
                    return Respond(sender, $"Removed {removed} custom NPC(s).");
                default:
                    return Respond(sender, HelpText());
            }
        }

        private bool SpawnFromCommand(ulong sender, string archetype)
        {
            if (!Npcs.IsKnownArchetype(archetype))
                return Respond(sender, $"Unknown NPC archetype '{archetype}'. Available: {SiNpcManager.SoldierDummyArchetype}.");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return Respond(sender, "You must control a character to spawn an NPC.");

            var transform = CreateSpawnTransform(playerPosition.WorldMatrix);
            var entityId = MyEntityIdentifier.AllocateId();
            if (!Npcs.TrySpawn(archetype, entityId, transform, out var npc))
                return Respond(sender, $"Failed to spawn custom NPC '{archetype}'; its model or entity definition could not be loaded.");

            BroadcastSpawn(npc);
            return Respond(sender, $"Spawned {archetype} ({entityId}).");
        }

        private static MatrixD CreateSpawnTransform(in MatrixD playerTransform)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(playerTransform.Translation);
            var up = gravity.LengthSquared() > 0.0001f
                ? -Vector3D.Normalize(gravity)
                : playerTransform.Up;

            var playerForward = Vector3D.Reject(playerTransform.Forward, up);
            if (playerForward.LengthSquared() < 0.0001)
                playerForward = Vector3D.CalculatePerpendicularVector(up);
            playerForward.Normalize();

            var position = playerTransform.Translation + playerForward * SpawnDistance;
            return MatrixD.CreateWorld(position, -playerForward, up);
        }

        private static string HelpText() =>
            $"{Command} spawn [{SiNpcManager.SoldierDummyArchetype}] | list | clear";

        private bool Respond(ulong sender, string response)
        {
            _chat?.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"), 0, response);
            return true;
        }

        private static void BroadcastSpawn(SiNpc npc)
        {
            if (MyMultiplayerModApi.Static == null)
                return;

            var transform = npc.Transform;
            MyMultiplayerModApi.Static.RaiseStaticEvent(
                x => SpawnNpcClient,
                npc.EntityId,
                npc.Archetype,
                transform);
        }

        private static void BroadcastClear()
        {
            if (MyMultiplayerModApi.Static != null)
                MyMultiplayerModApi.Static.RaiseStaticEvent(x => ClearNpcsClient);
        }

        [Event, Reliable, Broadcast]
        private static void SpawnNpcClient(long entityId, string archetype, MatrixD transform)
        {
            _instance?.Npcs?.TrySpawn(archetype, entityId, transform, out _);
        }

        [Event, Reliable, Broadcast]
        private static void ClearNpcsClient()
        {
            _instance?.Npcs?.CloseAll();
        }

        [Event, Reliable, Server]
        private static void RequestNpcSnapshot()
        {
            if (_instance?.Npcs == null || MyMultiplayerModApi.Static == null)
                return;

            var endpoint = MyEventContext.Current.Sender;
            foreach (var npc in _instance.Npcs.Npcs.Values)
            {
                var transform = npc.Transform;
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => SpawnNpcSnapshotClient,
                    npc.EntityId,
                    npc.Archetype,
                    transform,
                    endpoint);
            }
        }

        [Event, Reliable, Client]
        private static void SpawnNpcSnapshotClient(long entityId, string archetype, MatrixD transform)
        {
            SpawnNpcClient(entityId, archetype, transform);
        }
    }
}
