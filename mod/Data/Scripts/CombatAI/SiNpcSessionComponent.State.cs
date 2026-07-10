using System;
using Medieval.GameSystems.Factions;
using Sandbox.Game.Entities;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        internal SiSquadEngagementStance GetEngagementStance(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return SiSquadEngagementStance.Enemies;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return SiSquadEngagementStance.Enemies;

            SiSquadCommandState state;
            return _squadOrders.TryGetValue(assignment.Leader.Id, out state)
                ? state.EngagementStance
                : SiSquadEngagementStance.Enemies;
        }

        internal SiSquadCombatStance GetCombatStance(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return SiSquadCombatStance.Safe;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment))
                return SiSquadCombatStance.Safe;

            return GetOrCreateCombatState(assignment.Leader).Stance;
        }

        private SiSquadCombatStance GetCombatStance(SiSquadLeaderKey leader) =>
            GetOrCreateCombatState(leader).Stance;

        internal bool IsRearmOverrideActive(SiNpc npc)
        {
            return TryGetPlayerCommandState(npc, out _, out var state)
                   && state != null
                   && state.RearmOverride;
        }

        internal bool IsRearming(SiNpc npc)
        {
            if (!IsRearmOverrideActive(npc))
                return false;

            return SiNpcAmmoStatusHelper.TryGetAmmoStatus(npc, out var ammoStatus)
                   && ammoStatus.NeedsRearm;
        }

        internal bool IsFollowingPlayer(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            SiSquadCommandState state;
            return _squadOrders.TryGetValue(assignment.Leader.Id, out state)
                   && state.Mode == SiSquadOrderMode.Follow;
        }

        internal bool TryGetAssignment(SiNpc npc, out SiAssignedNpc assignment)
        {
            assignment = null;
            return npc != null
                   && Squads != null
                   && Squads.TryGetAssignment(npc.EntityId, out assignment);
        }

        internal bool CanPlayerAccessNpcInventory(MyEntity npcEntity, long playerIdentityId)
        {
            if (npcEntity == null || playerIdentityId == 0 || Npcs == null || Squads == null)
                return false;

            return Npcs.Npcs.ContainsKey(npcEntity.EntityId)
                   && Squads.TryGetAssignment(npcEntity.EntityId, out var assignment)
                   && assignment.Leader.Kind == SiSquadLeaderKind.Player
                   && assignment.Leader.Id == playerIdentityId;
        }

        internal bool IsAiSquadLeader(SiNpc npc)
        {
            SiAssignedNpc assignment;
            return TryGetAssignment(npc, out assignment)
                   && assignment.IsLeader
                   && assignment.Leader.Kind == SiSquadLeaderKind.Ai;
        }

        internal bool TryGetLeaderPosition(SiNpc npc, out Vector3D position)
        {
            position = Vector3D.Zero;
            if (npc == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment))
                return false;

            return TryGetLeaderPosition(assignment.Leader, out position);
        }

        internal bool TryGetLeaderPosition(SiSquadLeaderKey leader, out Vector3D position)
        {
            position = Vector3D.Zero;
            if (leader.Id == 0)
                return false;

            if (leader.Kind == SiSquadLeaderKind.Player)
            {
                VRageMath.MatrixD leaderTransform;
                if (!TryGetLeaderTransform(leader.Id, out leaderTransform))
                    return false;

                position = leaderTransform.Translation;
                return true;
            }

            SiNpc leaderNpc;
            if (Npcs == null
                || !Npcs.Npcs.TryGetValue(leader.Id, out leaderNpc)
                || leaderNpc?.Entity == null)
                return false;

            position = leaderNpc.Entity.WorldMatrix.Translation;
            return true;
        }

        internal bool TryGetLeaderDistance(SiNpc npc, out double distance)
        {
            distance = 0;
            Vector3D leaderPosition;
            if (npc?.Entity == null || !TryGetLeaderPosition(npc, out leaderPosition))
                return false;

            distance = Vector3D.Distance(npc.Entity.WorldMatrix.Translation, leaderPosition);
            return true;
        }

        internal long GetCombatEntryToken(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return 0;

            SiAssignedNpc assignment;
            return Squads.TryGetAssignment(npc.EntityId, out assignment)
                ? GetOrCreateCombatState(assignment.Leader).CombatEntryToken
                : 0;
        }

        internal bool IsCoverAvailable(SiNpc requester, in Vector3D coverPosition, double radius)
        {
            if (requester == null)
                return false;
            if (IsLeaderCoverBlacklisted(requester, coverPosition))
                return false;

            foreach (var reservation in _coverReservations)
            {
                if (reservation.Key == requester.EntityId)
                    continue;

                var claimed = reservation.Value;
                var threshold = Math.Max(radius, claimed.Radius);
                if (Vector3D.DistanceSquared(claimed.Position, coverPosition) <= threshold * threshold)
                    return false;
            }

            return true;
        }

        internal long GetLastSquadShotAtTime(SiNpc npc)
        {
            if (npc == null || Squads == null)
                return long.MinValue;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(npc.EntityId, out assignment))
                return long.MinValue;

            if (!_squadCombatStates.TryGetValue(assignment.Leader, out var combatState) || combatState == null)
                return long.MinValue;

            return combatState.LastShotAtTime;
        }

        internal bool WasSquadRecentlyShotAt(SiNpc npc, long recentMilliseconds)
        {
            if (recentMilliseconds <= 0)
                return false;

            var lastShotAtTime = GetLastSquadShotAtTime(npc);
            return lastShotAtTime > long.MinValue
                   && CurrentTimeMilliseconds() - lastShotAtTime <= recentMilliseconds;
        }

        internal bool TryReserveCover(SiNpc requester, in Vector3D coverPosition, double radius)
        {
            if (requester == null)
                return false;
            if (!IsCoverAvailable(requester, coverPosition, radius))
                return false;

            _coverReservations[requester.EntityId] = new SiCoverReservation(coverPosition, radius);
            return true;
        }

        internal void ReleaseCover(long entityId)
        {
            if (entityId != 0)
                _coverReservations.Remove(entityId);
        }

        internal void CacheFormationPosition(SiNpc npc, in Vector3D position)
        {
            CachePosition(npc?.EntityId ?? 0, SiNpcCachedPositionKind.Formation, position);
        }

        internal void CacheCombatPosition(SiNpc npc, SiCombatMovementRole role, in Vector3D position)
        {
            CachePosition(npc?.EntityId ?? 0, ToCachedPositionKind(role), position);
        }

        internal void ClearCachedCombatPosition(long entityId, SiCombatMovementRole role)
        {
            ClearCachedPosition(entityId, ToCachedPositionKind(role));
        }

        internal bool HasNearbyCachedCombatPosition(
            SiNpc requester,
            in Vector3D position,
            double minimumDistance,
            out long blockingEntityId)
        {
            blockingEntityId = 0;
            if (minimumDistance <= 0 || _positionCache.Count == 0)
                return false;

            var requesterId = requester?.EntityId ?? 0;
            var minimumDistanceSquared = minimumDistance * minimumDistance;
            foreach (var entry in _positionCache)
            {
                if (entry.Key == requesterId || entry.Value == null)
                    continue;

                if (entry.Value.HasCover
                    && Vector3D.DistanceSquared(entry.Value.CoverPosition, position) < minimumDistanceSquared)
                {
                    blockingEntityId = entry.Key;
                    return true;
                }

                if (entry.Value.HasPlainView
                    && Vector3D.DistanceSquared(entry.Value.PlainViewPosition, position) < minimumDistanceSquared)
                {
                    blockingEntityId = entry.Key;
                    return true;
                }
            }

            return false;
        }

        internal bool TryFollowCachedFormationPosition(SiNpc npc, double refreshDistanceSquared)
        {
            return TryFollowCachedPosition(npc, SiNpcCachedPositionKind.Formation, refreshDistanceSquared);
        }

        internal bool TryFollowCachedCombatPosition(
            SiNpc npc,
            SiCombatMovementRole role,
            double refreshDistanceSquared)
        {
            return TryFollowCachedPosition(npc, ToCachedPositionKind(role), refreshDistanceSquared);
        }

        internal bool HasSquadmateInThrowDanger(
            SiNpc requester,
            long requesterEntityId,
            in Vector3D throwOrigin,
            in Vector3D targetPosition,
            double blastRadius,
            double trajectoryRadius,
            out long blockingEntityId,
            out Vector3D blockingPosition)
        {
            blockingEntityId = 0;
            blockingPosition = Vector3D.Zero;
            if (requester == null || Squads == null || Npcs == null)
                return false;

            SiAssignedNpc requesterAssignment;
            if (!Squads.TryGetAssignment(requester.EntityId, out requesterAssignment))
                return false;

            foreach (var entry in Npcs.Npcs)
            {
                var squadmate = entry.Value;
                if (squadmate == null || entry.Key == requesterEntityId)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(entry.Key, out assignment)
                    || !assignment.Leader.Equals(requesterAssignment.Leader))
                    continue;

                if (TryGetUnsafeThrowPosition(
                        entry.Key,
                        squadmate,
                        throwOrigin,
                        targetPosition,
                        blastRadius,
                        trajectoryRadius,
                        out blockingPosition))
                {
                    blockingEntityId = entry.Key;
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetCachedCoverSearch(
            in Vector3D searchOrigin,
            double searchRadius,
            in Vector3D threatPosition,
            long threatEntityId,
            MyDefinitionId behaviorDefinitionId,
            out SiCoverSearchCacheEntry entry)
        {
            entry = null;
            var key = new SiCoverSearchCacheKey(
                searchOrigin,
                searchRadius,
                threatPosition,
                threatEntityId,
                behaviorDefinitionId,
                CoverSearchCachePositionQuantization);
            SiCoverSearchCacheEntry cached;
            if (!_coverSearchCache.TryGetValue(key, out cached))
                return false;

            var now = CurrentTimeMilliseconds();
            if (cached == null || cached.ExpiresAtMilliseconds < now)
            {
                _coverSearchCache.Remove(key);
                return false;
            }

            entry = cached;
            return true;
        }

        internal void StoreCachedCoverSearch(
            in Vector3D searchOrigin,
            double searchRadius,
            in Vector3D threatPosition,
            long threatEntityId,
            MyDefinitionId behaviorDefinitionId,
            SiCoverSearchCacheEntry entry)
        {
            if (entry == null)
                return;

            entry.ExpiresAtMilliseconds = CurrentTimeMilliseconds() + CoverSearchCacheLifetimeMilliseconds;
            var key = new SiCoverSearchCacheKey(
                searchOrigin,
                searchRadius,
                threatPosition,
                threatEntityId,
                behaviorDefinitionId,
                CoverSearchCachePositionQuantization);
            _coverSearchCache[key] = entry;
        }

        internal bool TryGetCachedCoverScan(
            in Vector3D searchOrigin,
            double searchRadius,
            MyDefinitionId behaviorDefinitionId,
            out SiCoverScanCacheEntry entry)
        {
            entry = null;
            var key = new SiCoverScanCacheKey(
                searchOrigin,
                searchRadius,
                behaviorDefinitionId,
                CoverScanCachePositionQuantization);
            SiCoverScanCacheEntry cached;
            if (!_coverScanCache.TryGetValue(key, out cached))
                return false;

            var now = CurrentTimeMilliseconds();
            if (cached == null || cached.ExpiresAtMilliseconds < now)
            {
                _coverScanCache.Remove(key);
                return false;
            }

            entry = cached;
            return true;
        }

        internal void StoreCachedCoverScan(
            in Vector3D searchOrigin,
            double searchRadius,
            MyDefinitionId behaviorDefinitionId,
            SiCoverScanCacheEntry entry)
        {
            if (entry == null)
                return;

            entry.ExpiresAtMilliseconds = CurrentTimeMilliseconds() + CoverScanCacheLifetimeMilliseconds;
            var key = new SiCoverScanCacheKey(
                searchOrigin,
                searchRadius,
                behaviorDefinitionId,
                CoverScanCachePositionQuantization);
            _coverScanCache[key] = entry;
        }

        private bool IsLeaderCoverBlacklisted(SiNpc requester, in Vector3D coverPosition)
        {
            if (requester == null || Squads == null)
                return false;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(requester.EntityId, out assignment) || assignment.IsLeader)
                return false;

            var radius = SiAdvanceTowardEnemyLeaderBehaviorDefinition.LoadDefault()?.LeaderCoverBlacklistRadius ?? 0f;
            if (radius <= 0)
                return false;

            Vector3D leaderPosition;
            if (!TryGetLeaderPosition(assignment.Leader, out leaderPosition))
                return false;

            return Vector3D.DistanceSquared(leaderPosition, coverPosition) <= radius * radius;
        }

        private bool TryGetPlayerCommandState(
            SiNpc npc,
            out SiAssignedNpc assignment,
            out SiSquadCommandState state)
        {
            assignment = null;
            state = null;
            if (npc == null || Squads == null)
                return false;

            if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                || assignment.Leader.Kind != SiSquadLeaderKind.Player)
                return false;

            return _squadOrders.TryGetValue(assignment.Leader.Id, out state);
        }

        private static void SetRearmOverride(SiSquadCommandState state, bool enabled)
        {
            if (state != null)
                state.RearmOverride = enabled;
        }
    }
}
