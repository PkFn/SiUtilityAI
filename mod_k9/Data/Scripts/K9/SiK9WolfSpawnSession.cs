using System;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.Chat;
using Sandbox.Game.Players;
using VRage;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.ObjectBuilders;
using VRage.Scene;
using VRage.Session;
using VRage.Utils;
using VRageMath;

namespace Si.K9
{
    [MySessionComponent(AllowAutomaticCreation = true, AlwaysOn = true)]
    [MyDependency(typeof(MyChatSystem), Critical = false)]
    public sealed class SiK9WolfSpawnSession : MySessionComponent
    {
        private static readonly MyDefinitionId WolfDefinition =
            new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), "SiK9Wolf");

        [Automatic]
        private readonly MyChatSystem _chat = null;

        protected override void OnLoad()
        {
            base.OnLoad();
            _chat?.RegisterChatCommand(
                "/si-k9",
                HandleCommand,
                "Spawns Si K9 test entities.",
                MyChatCommandType.Server);
        }

        private bool HandleCommand(ulong sender, string message, MyChatCommandType handledAsType)
        {
            if (handledAsType != MyChatCommandType.Server)
                return false;

            var args = (message ?? string.Empty).Trim();
            if (!string.Equals(args, "wolf", StringComparison.OrdinalIgnoreCase))
                return Respond(sender, "Usage: /si-k9 wolf");

            var player = MyPlayers.Static.GetPlayer(new MyPlayer.PlayerId(sender, 0));
            var playerPosition = player?.ControlledEntity?.Get<MyPositionComponentBase>();
            if (playerPosition == null)
                return Respond(sender, "You must control a character to spawn a K9 wolf.");

            try
            {
                var matrix = playerPosition.WorldMatrix;
                var spawnPosition = matrix.Translation + matrix.Forward * 3 + matrix.Up * 0.2;
                var spawnMatrix = MatrixD.CreateWorld(spawnPosition, matrix.Forward, matrix.Up);
                var wolf = new MyObjectBuilder_EntityBase
                {
                    EntityId = MyEntityIdentifier.AllocateId(),
                    EntityDefinitionId = WolfDefinition,
                    PersistentFlags = MyPersistentEntityFlags2.InScene,
                    PositionAndOrientation = new MyPositionAndOrientation(spawnMatrix),
                };

                var entity = MyEntities.CreateFromObjectBuilder(wolf);
                if (entity == null)
                    return Respond(sender, "Failed to create the K9 wolf entity.");

                MyEntities.Add(entity, true);
                return Respond(sender, $"Spawned K9 wolf ({entity.EntityId}).");
            }
            catch (Exception exception)
            {
                return Respond(sender, $"Failed to spawn K9 wolf: {exception.Message}");
            }
        }

        private bool Respond(ulong sender, string text)
        {
            if (!string.IsNullOrEmpty(text))
                _chat?.SendMessageToClient(sender, MyStringHash.GetOrCompute("System"), 0, text);
            return true;
        }
    }
}
