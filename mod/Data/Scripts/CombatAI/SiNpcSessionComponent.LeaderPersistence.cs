using System.Collections.Generic;
using Medieval.Entities.Components;
using VRage.Game.Components;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private const float AiLeaderPersistenceInflateLength = 1.5f;

        private readonly HashSet<long> _persistentAiLeaderIds = new HashSet<long>();
        private readonly HashSet<long> _desiredPersistentAiLeaderIds = new HashSet<long>();
        private readonly List<long> _stalePersistentAiLeaderIds = new List<long>();

        private void UpdateAiLeaderPersistence()
        {
            if (Npcs == null || Squads == null)
            {
                ClearAiLeaderPersistence();
                return;
            }

            _desiredPersistentAiLeaderIds.Clear();
            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc == null)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                    || assignment.Leader.Kind != SiSquadLeaderKind.Ai
                    || assignment.Leader.Id == 0)
                    continue;

                _desiredPersistentAiLeaderIds.Add(assignment.Leader.Id);
                EnsureAiLeaderPersistence(assignment.Leader.Id);
            }

            _stalePersistentAiLeaderIds.Clear();
            foreach (var leaderId in _persistentAiLeaderIds)
                if (!_desiredPersistentAiLeaderIds.Contains(leaderId))
                    _stalePersistentAiLeaderIds.Add(leaderId);

            for (var i = 0; i < _stalePersistentAiLeaderIds.Count; i++)
                RemoveAiLeaderPersistence(_stalePersistentAiLeaderIds[i]);

            _stalePersistentAiLeaderIds.Clear();
            _desiredPersistentAiLeaderIds.Clear();
        }

        private void ClearAiLeaderPersistence()
        {
            if (_persistentAiLeaderIds.Count == 0)
                return;

            _stalePersistentAiLeaderIds.Clear();
            foreach (var leaderId in _persistentAiLeaderIds)
                _stalePersistentAiLeaderIds.Add(leaderId);

            for (var i = 0; i < _stalePersistentAiLeaderIds.Count; i++)
                RemoveAiLeaderPersistence(_stalePersistentAiLeaderIds[i]);

            _stalePersistentAiLeaderIds.Clear();
            _desiredPersistentAiLeaderIds.Clear();
        }

        private void EnsureAiLeaderPersistence(long leaderId)
        {
            if (Npcs == null)
                return;

            SiNpc leaderNpc;
            if (!Npcs.Npcs.TryGetValue(leaderId, out leaderNpc))
            {
                _persistentAiLeaderIds.Remove(leaderId);
                return;
            }

            var entity = leaderNpc.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
            {
                _persistentAiLeaderIds.Remove(leaderId);
                return;
            }

            if (entity.Components.Contains<MyInfinitePersistenceViewComponent>())
                return;

            var persistence = new MyInfinitePersistenceViewComponent();
            entity.Components.Add(persistence);
            persistence.RemoveOnSleep = false;
            persistence.InflateLength = AiLeaderPersistenceInflateLength;
            _persistentAiLeaderIds.Add(leaderId);
        }

        private void RemoveAiLeaderPersistence(long leaderId)
        {
            if (!_persistentAiLeaderIds.Remove(leaderId) || Npcs == null)
                return;

            SiNpc leaderNpc;
            if (!Npcs.Npcs.TryGetValue(leaderId, out leaderNpc))
                return;

            var entity = leaderNpc.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return;

            if (!entity.Components.Contains<MyInfinitePersistenceViewComponent>())
                return;

            entity.Components.Remove(entity.Components.Get<MyInfinitePersistenceViewComponent>());
        }
    }
}
