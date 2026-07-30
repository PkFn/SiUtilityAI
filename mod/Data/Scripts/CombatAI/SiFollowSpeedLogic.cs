using Sandbox.Game.EntityComponents.Character;
using Sandbox.Game.Players;

namespace Si.UtilityAI
{
    public static class SiFollowSpeedLogic
    {
        public static SiNpcMovementSpeed ResolveFollowerSpeed(
            SiSquadSystemDefinition definition,
            SiNpcMovementSpeed checkpointSpeed,
            double checkpointDistance)
        {
            return definition != null
                ? definition.ResolveFormationSpeed(checkpointSpeed, checkpointDistance)
                : checkpointSpeed;
        }

        public static SiNpcMovementSpeed GetPlayerCheckpointSpeed(MyPlayer player)
        {
            var movement = player?.ControlledEntity?.Get<MyCharacterMovementComponent>();
            if (movement == null)
                return SiNpcMovementSpeed.Run;
            if (movement.IsSprinting)
                return SiNpcMovementSpeed.Sprint;
            if (movement.IsWalking)
                return SiNpcMovementSpeed.Walk;
            if (movement.IsRunning)
                return SiNpcMovementSpeed.Run;

            return SiNpcMovementSpeed.Run;
        }
    }
}
