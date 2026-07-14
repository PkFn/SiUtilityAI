using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.Players;
using Sandbox.ModAPI;
using VRage.Entities.Gravity;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.Components;
using VRage.ModAPI;
using VRageMath;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private void UpdateCombatStances()
        {
            if (_squadCombatStates.Count == 0)
                return;

            var now = CurrentTimeMilliseconds();
            foreach (var entry in _squadCombatStates)
            {
                var leader = entry.Key;
                var state = entry.Value;
                if (state == null || state.Stance != SiSquadCombatStance.Combat)
                    continue;

                var lastThreatTime = Math.Max(state.LastShotAtTime, state.LastEnemySpottedTime);
                var lastRelevantTime = Math.Max(lastThreatTime, state.LastStanceChangeTime);
                if (now - lastRelevantTime < CombatStanceCooldownMilliseconds)
                    continue;
                if (HasSpottedEnemyNearby(leader))
                    continue;

                SetCombatStance(
                    leader,
                    state.LeaderName,
                    SiSquadCombatStance.Safe,
                    SiSquadCombatTransitionReason.AreaClear,
                    false);
            }
        }

        private bool HasSpottedEnemyNearby(SiSquadLeaderKey leader)
        {
            if (Npcs == null || Squads == null || Spotting == null)
                return false;

            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc?.Entity == null)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(npc.EntityId, out assignment) || !assignment.Leader.Equals(leader))
                    continue;
                if (Spotting.HasSpottedTargetNearby(npc.EntityId, CombatStanceNearbyEnemyDistance))
                    return true;
            }

            return false;
        }

        private void SetCombatStance(
            SiSquadLeaderKey leader,
            string leaderName,
            SiSquadCombatStance stance,
            SiSquadCombatTransitionReason reason,
            bool speakAsPlayerOrder,
            bool suppressSpeech = false)
        {
            var state = GetOrCreateCombatState(leader);
            if (!string.IsNullOrWhiteSpace(leaderName))
                state.LeaderName = leaderName;

            var previous = state.Stance;
            state.Stance = stance;
            state.LastStanceChangeTime = CurrentTimeMilliseconds();
            if (stance == SiSquadCombatStance.Combat && reason == SiSquadCombatTransitionReason.PlayerOrder)
                state.LastEnemySpottedTime = state.LastStanceChangeTime;

            if (previous == stance)
                return;

            if (stance == SiSquadCombatStance.Combat)
            {
                state.CombatEntryToken++;
                ClearSquadWaypoints(leader);
            }

            if (suppressSpeech)
                return;

            if (speakAsPlayerOrder)
                SpeakSquadBehaviorChange(leader, PlayerOrderCombatStanceReport(stance), true);
            else
                SpeakSquadBehaviorChange(leader, CombatStanceChangeReport(stance, reason), false);
        }

        private SiSquadCombatState GetOrCreateCombatState(SiSquadLeaderKey leader)
        {
            SiSquadCombatState state;
            if (_squadCombatStates.TryGetValue(leader, out state))
                return state;

            state = new SiSquadCombatState
            {
                LeaderName = leader.Kind == SiSquadLeaderKind.Player ? "Player " + leader.Id : "Squad",
                Stance = SiSquadCombatStance.Safe,
                LastShotAtTime = long.MinValue,
                LastEnemySpottedTime = long.MinValue,
                LastStanceChangeTime = long.MinValue,
                CombatEntryToken = 0,
            };
            _squadCombatStates.Add(leader, state);
            return state;
        }

        private void SpeakSquadBehaviorChange(
            SiSquadLeaderKey leader,
            string message,
            bool force)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var speaker = FindSquadSpeaker(leader);
            if (speaker != null)
            {
                if (force || ShowSquadChatter)
                    speaker.TrySpeak(message);
            }
        }

        private SiNpc FindSquadSpeaker(SiSquadLeaderKey leader)
        {
            if (Npcs == null || Squads == null)
                return null;

            SiNpc first = null;
            foreach (var npc in Npcs.Npcs.Values)
            {
                SiAssignedNpc assignment;
                if (npc == null
                    || !Squads.TryGetAssignment(npc.EntityId, out assignment)
                    || !assignment.Leader.Equals(leader))
                    continue;
                if (assignment.IsLeader)
                    return npc;
                if (first == null)
                    first = npc;
            }

            return first;
        }

        internal void ReportNpcSpottedTarget(long observerEntityId, long targetEntityId)
        {
            if (!IsAuthoritative || observerEntityId == 0 || targetEntityId == 0 || Npcs == null || Squads == null)
                return;

            SiAssignedNpc assignment;
            if (!Npcs.Npcs.ContainsKey(observerEntityId)
                || !Squads.TryGetAssignment(observerEntityId, out assignment))
                return;

            var state = GetOrCreateCombatState(assignment.Leader);
            state.LastEnemySpottedTime = CurrentTimeMilliseconds();
            SetCombatStance(
                assignment.Leader,
                assignment.LeaderName,
                SiSquadCombatStance.Combat,
                SiSquadCombatTransitionReason.EnemySpotted,
                false);
        }

        internal void ReportNpcFiredShot(long entityId)
        {
            if (!IsAuthoritative || entityId == 0 || Squads == null)
                return;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(entityId, out assignment))
                return;

            var state = GetOrCreateCombatState(assignment.Leader);
            state.LastEnemySpottedTime = CurrentTimeMilliseconds();
            SetCombatStance(
                assignment.Leader,
                assignment.LeaderName,
                SiSquadCombatStance.Combat,
                SiSquadCombatTransitionReason.OpeningFire,
                false);
        }

        internal void ReportNpcShotAt(long entityId)
        {
            if (!IsAuthoritative || entityId == 0 || Squads == null)
                return;

            SiAssignedNpc assignment;
            if (!Squads.TryGetAssignment(entityId, out assignment))
                return;

            var state = GetOrCreateCombatState(assignment.Leader);
            state.LastShotAtTime = CurrentTimeMilliseconds();
            SetCombatStance(
                assignment.Leader,
                assignment.LeaderName,
                SiSquadCombatStance.Combat,
                SiSquadCombatTransitionReason.TakingFire,
                false);
        }

        private void UpdateSquadOrders()
        {
            foreach (var entry in _squadOrders)
            {
                var state = entry.Value;
                if (state.Mode != SiSquadOrderMode.Follow || state.TransportMode != SiSquadTransportMode.None)
                    continue;
                var leader = PlayerLeaderKey(entry.Key);
                var combatStance = GetCombatStance(leader);
                if (combatStance == SiSquadCombatStance.Combat)
                    continue;

                string failure;
                ApplyFollowOrder(entry.Key, state, false, out failure);
            }

            UpdateAiSquadOrders();
        }

        private void UpdateAiSquadOrders()
        {
            if (Npcs == null || Squads?.Definition == null)
                return;

            var leaders = new HashSet<SiSquadLeaderKey>();
            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc == null)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                    || assignment.Leader.Kind != SiSquadLeaderKind.Ai
                    || !leaders.Add(assignment.Leader))
                    continue;

                if (GetCombatStance(assignment.Leader) == SiSquadCombatStance.Combat)
                    continue;

                MaintainAiLeaderMoveOrder(assignment.Leader);
                ApplyAiFollowOrder(assignment.Leader);
            }
        }

        private int ApplyAiFollowOrder(SiSquadLeaderKey leader)
        {
            if (Npcs == null || Squads?.Definition == null || leader.Kind != SiSquadLeaderKind.Ai)
                return 0;

            if (!Npcs.Npcs.TryGetValue(leader.Id, out var leaderNpc)
                || leaderNpc?.Entity == null
                || leaderNpc.Entity.Closed
                || leaderNpc.Entity.MarkedForClose)
                return 0;

            var troops = new List<SiNpc>();
            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc == null || npc.EntityId == leader.Id)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                    || !assignment.Leader.Equals(leader))
                    continue;

                troops.Add(npc);
            }

            if (troops.Count == 0)
                return 0;

            var leaderTransform = leaderNpc.Entity.WorldMatrix;
            var leaderMotion = UpdateLeaderMotionState(leader.Id, leaderTransform);
            Vector3D origin;
            Vector3D forward;
            Vector3D right;
            CreateLeaderFrame(leaderTransform, leaderMotion.Direction, out origin, out forward, out right);

            var definition = Squads.Definition;
            var refreshDistanceSquared = definition.WaypointRefreshDistance * definition.WaypointRefreshDistance;
            return ApplyChainedFollowOrder(
                troops,
                SiSquadFormation.Column,
                origin,
                forward,
                definition,
                refreshDistanceSquared);
        }

        private void HandleInactivePlayerLedSquads()
        {
            if (Squads == null || Npcs == null || MyPlayers.Static == null)
                return;

            _stalePlayerLeaderIds.Clear();
            foreach (var entry in _playerLeaderStates)
                _stalePlayerLeaderIds.Add(entry.Key);

            foreach (var playerEntry in MyPlayers.Static.GetAllPlayers())
            {
                var player = playerEntry.Value;
                var identity = player?.Identity;
                if (identity == null)
                    continue;

                var leaderIdentityId = identity.Id;
                if (!Squads.HasLeaderNpcs(Npcs, leaderIdentityId))
                {
                    _playerLeaderStates.Remove(leaderIdentityId);
                    _stalePlayerLeaderIds.Remove(leaderIdentityId);
                    continue;
                }

                SiPlayerLeaderState state;
                if (!_playerLeaderStates.TryGetValue(leaderIdentityId, out state))
                    _playerLeaderStates[leaderIdentityId] = state = new SiPlayerLeaderState();

                var isActive = IsPlayerLeaderActive(leaderIdentityId);
                if (state.WasActive && !isActive)
                    ApplyAutomaticPlayerSquadFallback(leaderIdentityId, PlayerName(player));

                state.WasActive = isActive;
                _stalePlayerLeaderIds.Remove(leaderIdentityId);
            }

            for (var i = 0; i < _stalePlayerLeaderIds.Count; i++)
            {
                var leaderIdentityId = _stalePlayerLeaderIds[i];
                if (!Squads.HasLeaderNpcs(Npcs, leaderIdentityId))
                    _playerLeaderStates.Remove(leaderIdentityId);
            }

            _stalePlayerLeaderIds.Clear();
        }

        private void ApplyAutomaticPlayerSquadFallback(long leaderIdentityId, string leaderName)
        {
            if (leaderIdentityId == 0 || Squads == null || Npcs == null || !Squads.HasLeaderNpcs(Npcs, leaderIdentityId))
                return;

            StopSquad(0, leaderIdentityId);
            SetCombatStance(
                PlayerLeaderKey(leaderIdentityId),
                leaderName,
                SiSquadCombatStance.Combat,
                SiSquadCombatTransitionReason.PlayerOrder,
                false,
                true);
        }

        private void CleanupTransportStates()
        {
            if (_transportNpcStates.Count == 0 || Npcs == null)
                return;

            _staleCoverReservationIds.Clear();
            foreach (var entry in _transportNpcStates)
            {
                if (!Npcs.Npcs.TryGetValue(entry.Key, out var npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose
                    || npc.IsDead)
                {
                    _staleCoverReservationIds.Add(entry.Key);
                    continue;
                }

                var vehicleId = entry.Value?.VehicleEntityId ?? 0;
                if (vehicleId == 0 || MyEntities.GetEntityByIdOrDefault(vehicleId) == null)
                    _staleCoverReservationIds.Add(entry.Key);
            }

            for (var i = 0; i < _staleCoverReservationIds.Count; i++)
                _transportNpcStates.Remove(_staleCoverReservationIds[i]);
            _staleCoverReservationIds.Clear();
        }

        private void CleanupPositionCache()
        {
            if (_positionCache.Count == 0 || Npcs == null)
                return;

            _staleCoverReservationIds.Clear();
            foreach (var entry in _positionCache)
            {
                if (!Npcs.Npcs.TryGetValue(entry.Key, out var npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose
                    || npc.IsDead
                    || entry.Value == null
                    || entry.Value.IsEmpty)
                {
                    _staleCoverReservationIds.Add(entry.Key);
                }
            }

            for (var i = 0; i < _staleCoverReservationIds.Count; i++)
                _positionCache.Remove(_staleCoverReservationIds[i]);
            _staleCoverReservationIds.Clear();
        }

        private void CleanupCoverReservations()
        {
            if (_coverReservations.Count == 0 || Npcs == null)
                return;

            var cleanupStartedAt = DebugTimestampTicks();
            _staleCoverReservationIds.Clear();
            foreach (var reservation in _coverReservations)
            {
                SiNpc npc;
                if (!Npcs.Npcs.TryGetValue(reservation.Key, out npc)
                    || npc?.Entity == null
                    || npc.Entity.Closed
                    || npc.Entity.MarkedForClose
                    || npc.IsDead
                    || GetCombatStance(npc) != SiSquadCombatStance.Combat)
                {
                    _staleCoverReservationIds.Add(reservation.Key);
                    continue;
                }
            }

            for (var i = 0; i < _staleCoverReservationIds.Count; i++)
                _coverReservations.Remove(_staleCoverReservationIds[i]);
            LogSlowCoverCleanup(DebugElapsedMilliseconds(cleanupStartedAt), _staleCoverReservationIds.Count);
            _staleCoverReservationIds.Clear();
        }

        private void ReassignLeaderlessSquads()
        {
            if (Squads == null || Npcs == null)
                return;

            var changes = Squads.ReassignLeaderlessSquads(Npcs, IsPlayerLeaderActive);
            if (changes == null || changes.Count == 0)
                return;

            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                ClearSquadWaypoints(change.NewLeader);
                MigrateSquadLeadershipState(change);
            }
        }

        private void MigrateSquadLeadershipState(SiSquadLeadershipChange change)
        {
            if (change.OldLeader.Kind == SiSquadLeaderKind.Player)
            {
                _squadOrders.Remove(change.OldLeader.Id);
                _leaderMotionStates.Remove(change.OldLeader.Id);
            }

            if (_aiSquadMoveOrders.TryGetValue(change.OldLeader, out var moveOrder))
            {
                _aiSquadMoveOrders.Remove(change.OldLeader);
                if (moveOrder != null)
                    _aiSquadMoveOrders[change.NewLeader] = moveOrder;
            }

            SiSquadCombatState combatState;
            if (!_squadCombatStates.TryGetValue(change.OldLeader, out combatState))
                return;

            _squadCombatStates.Remove(change.OldLeader);
            combatState.LeaderName = change.NewLeaderName;
            _squadCombatStates[change.NewLeader] = combatState;
        }

        private void LogSlowCoverCleanup(double elapsedMilliseconds, int removedReservations)
        {
            var now = CurrentTimeMilliseconds();
            if (elapsedMilliseconds < 2 && _coverReservations.Count < 16 && removedReservations == 0)
                return;
            if (_lastCoverCleanupLogTime >= 0 && now - _lastCoverCleanupLogTime < 5000)
                return;

            _lastCoverCleanupLogTime = now;
        }

        private static long DebugTimestampTicks()
        {
            return DateTime.UtcNow.Ticks;
        }

        private static double DebugElapsedMilliseconds(long startTicks)
        {
            return (DateTime.UtcNow.Ticks - startTicks) / (double)TimeSpan.TicksPerMillisecond;
        }

        private void CleanupExpiredCoverSearchCache()
        {
            if (_coverSearchCache.Count == 0)
                return;

            var now = CurrentTimeMilliseconds();
            _expiredCoverSearchCacheKeys.Clear();
            foreach (var entry in _coverSearchCache)
                if (entry.Value == null || entry.Value.ExpiresAtMilliseconds < now)
                    _expiredCoverSearchCacheKeys.Add(entry.Key);

            for (var i = 0; i < _expiredCoverSearchCacheKeys.Count; i++)
                _coverSearchCache.Remove(_expiredCoverSearchCacheKeys[i]);
            _expiredCoverSearchCacheKeys.Clear();
        }

        private void CleanupExpiredCoverScanCache()
        {
            if (_coverScanCache.Count == 0)
                return;

            var now = CurrentTimeMilliseconds();
            _expiredCoverScanCacheKeys.Clear();
            foreach (var entry in _coverScanCache)
                if (entry.Value == null || entry.Value.ExpiresAtMilliseconds < now)
                    _expiredCoverScanCacheKeys.Add(entry.Key);

            for (var i = 0; i < _expiredCoverScanCacheKeys.Count; i++)
                _coverScanCache.Remove(_expiredCoverScanCacheKeys[i]);
            _expiredCoverScanCacheKeys.Clear();
        }

        private int ApplyFollowOrder(
            long leaderIdentityId,
            SiSquadCommandState state,
            bool reportFailures,
            out string failure)
        {
            failure = null;
            if (Squads?.Definition == null)
            {
                failure = reportFailures ? "Squad data definition is missing." : null;
                return 0;
            }

            MatrixD leaderTransform;
            if (!TryGetLeaderTransform(leaderIdentityId, out leaderTransform))
            {
                failure = reportFailures ? "You must control a character to command a squad." : null;
                return 0;
            }

            var troops = Squads.GetLeaderNpcs(Npcs, leaderIdentityId);
            if (troops.Count == 0)
            {
                failure = reportFailures ? "Your squad has no utility AI troops." : null;
                return 0;
            }

            var leaderMotion = UpdateLeaderMotionState(leaderIdentityId, leaderTransform);
            Vector3D origin;
            Vector3D forward;
            Vector3D right;
            CreateLeaderFrame(leaderTransform, leaderMotion.Direction, out origin, out forward, out right);

            var definition = Squads.Definition;
            var refreshDistanceSquared = definition.WaypointRefreshDistance * definition.WaypointRefreshDistance;
            var issued = 0;
            if (state.Formation == SiSquadFormation.File
                || state.Formation == SiSquadFormation.Column
                || state.Formation == SiSquadFormation.StaggeredColumn)
            {
                issued = ApplyChainedFollowOrder(
                    troops,
                    state.Formation,
                    origin,
                    forward,
                    definition,
                    refreshDistanceSquared);
            }
            else
            {
                for (var i = 0; i < troops.Count; i++)
                {
                    var target = origin + FormationOffset(
                        state.Formation,
                        i,
                        troops.Count,
                        forward,
                        right,
                        definition);
                    if (TryCacheAndIssueFollowWaypoint(troops[i], target, refreshDistanceSquared))
                        issued++;
                }
            }

            return issued;
        }

        private int ApplyChainedFollowOrder(
            List<SiNpc> troops,
            SiSquadFormation formation,
            in Vector3D leaderPosition,
            in Vector3D leaderForward,
            SiSquadSystemDefinition definition,
            double refreshDistanceSquared)
        {
            var issued = 0;
            var anchorPosition = leaderPosition;
            var anchorForward = leaderForward;
            var followerGap = formation == SiSquadFormation.File
                ? definition.FileSpacing
                : definition.ColumnSpacing;
            if (followerGap <= 0)
                followerGap = definition.FollowDistance;

            for (var i = 0; i < troops.Count; i++)
            {
                var gap = i == 0 ? definition.FollowDistance : followerGap;
                var target = anchorPosition - anchorForward * gap;
                if (formation == SiSquadFormation.StaggeredColumn && definition.StaggeredColumnOffset > 0)
                {
                    var up = SurfaceUp(anchorPosition);
                    var right = NormalizedOrFallback(
                        Vector3D.Cross(anchorForward, up),
                        Vector3D.CalculatePerpendicularVector(anchorForward));
                    var side = i % 2 == 0 ? -1 : 1;
                    target += right * (side * definition.StaggeredColumnOffset);
                }
                if (TryCacheAndIssueFollowWaypoint(troops[i], target, refreshDistanceSquared))
                    issued++;

                var anchor = TryGetNpcFollowAnchor(troops[i], anchorForward);
                anchorPosition = anchor.Position;
                anchorForward = anchor.Forward;
            }

            return issued;
        }

        private void RequestAiSquadMoveOrder(SiSquadLeaderKey leader, in Vector3D target)
        {
            if (leader.Kind != SiSquadLeaderKind.Ai)
                return;

            var player = LocalPlayer();
            if (player?.Identity == null || !CanIdentityCommandArmy(player.Identity.Id, leader.Army))
                return;

            if (MyMultiplayerModApi.Static != null && !MyMultiplayerModApi.Static.IsServer)
            {
                MyMultiplayerModApi.Static.RaiseStaticEvent(
                    x => RequestAiSquadMoveOrderServer,
                    (byte)leader.Kind,
                    leader.Id,
                    (byte)leader.Army.Kind,
                    leader.Army.Id,
                    target);
                return;
            }

            ApplyAiSquadMoveOrder(player, leader, target);
        }

        private void ApplyAiSquadMoveOrder(MyPlayer issuer, SiSquadLeaderKey leader, in Vector3D target)
        {
            if (issuer?.Identity == null
                || leader.Kind != SiSquadLeaderKind.Ai
                || Npcs == null
                || Squads == null
                || !CanIdentityCommandArmy(issuer.Identity.Id, leader.Army)
                || !HasSquadMembers(leader))
                return;

            _aiSquadMoveOrders[leader] = new SiAiSquadMoveOrderState(target);
            SpeakAiMapMoveOrder(issuer, leader, target);
            MaintainAiLeaderMoveOrder(leader);
        }

        private bool MaintainAiLeaderMoveOrder(SiSquadLeaderKey leader)
        {
            if (leader.Kind != SiSquadLeaderKind.Ai
                || Npcs == null
                || !_aiSquadMoveOrders.TryGetValue(leader, out var state)
                || state == null)
                return false;

            if (!Npcs.Npcs.TryGetValue(leader.Id, out var leaderNpc)
                || leaderNpc?.Entity == null
                || leaderNpc.Entity.Closed
                || leaderNpc.Entity.MarkedForClose
                || leaderNpc.IsDead)
                return false;

            var definition = Squads?.Definition;
            var refreshDistance = definition?.WaypointRefreshDistance ?? 0;
            var arrivalDistance = Math.Max(
                AiMapCommandArrivalDistance,
                refreshDistance);
            if (Vector3D.DistanceSquared(
                    leaderNpc.Entity.WorldMatrix.Translation,
                    state.Target) <= arrivalDistance * arrivalDistance)
            {
                _aiSquadMoveOrders.Remove(leader);
                Npcs.TryClearWaypoint(leader.Id);
                return false;
            }

            var refreshDistanceSquared = refreshDistance * refreshDistance;
            var mover = leaderNpc as ISiWaypointMover;
            if (mover != null
                && mover.HasWaypoint
                && refreshDistanceSquared > 0
                && Vector3D.DistanceSquared(mover.Waypoint, state.Target) < refreshDistanceSquared)
                return true;

            return Npcs.TrySetWaypoint(leader.Id, state.Target);
        }

        private bool HasSquadMembers(SiSquadLeaderKey leader)
        {
            if (Npcs == null || Squads == null)
                return false;

            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc == null)
                    continue;

                SiAssignedNpc assignment;
                if (Squads.TryGetAssignment(npc.EntityId, out assignment) && assignment.Leader.Equals(leader))
                    return true;
            }

            return false;
        }

        private int ClearLeaderWaypoints(long leaderIdentityId)
        {
            var cleared = 0;
            foreach (var npc in Squads.GetLeaderNpcs(Npcs, leaderIdentityId))
            {
                var mover = npc as ISiWaypointMover;
                if (mover != null && !mover.HasWaypoint)
                {
                    cleared++;
                    continue;
                }

                if (Npcs.TryClearWaypoint(npc.EntityId))
                    cleared++;
            }

            return cleared;
        }

        private int ClearSquadWaypoints(SiSquadLeaderKey leader)
        {
            if (Npcs == null || Squads == null)
                return 0;

            var cleared = 0;
            foreach (var npc in Npcs.Npcs.Values)
            {
                if (npc == null)
                    continue;

                SiAssignedNpc assignment;
                if (!Squads.TryGetAssignment(npc.EntityId, out assignment)
                    || !assignment.Leader.Equals(leader))
                    continue;

                var mover = npc as ISiWaypointMover;
                if (mover != null && !mover.HasWaypoint)
                {
                    cleared++;
                    continue;
                }

                if (Npcs.TryClearWaypoint(npc.EntityId))
                    cleared++;
            }

            return cleared;
        }

        private SiSquadCommandState GetSquadOrder(long leaderIdentityId)
        {
            SiSquadCommandState state;
            if (!_squadOrders.TryGetValue(leaderIdentityId, out state))
                _squadOrders.Add(leaderIdentityId, state = new SiSquadCommandState());
            return state;
        }

        private bool TryCacheAndIssueFollowWaypoint(SiNpc npc, in Vector3D target, double refreshDistanceSquared)
        {
            if (npc == null || IsRearming(npc))
                return false;

            CacheFormationPosition(npc, target);
            return TryFollowCachedFormationPosition(npc, refreshDistanceSquared);
        }

        private void CachePosition(long entityId, SiNpcCachedPositionKind kind, in Vector3D position)
        {
            if (entityId == 0)
                return;

            if (!_positionCache.TryGetValue(entityId, out var state) || state == null)
                _positionCache[entityId] = state = new SiNpcPositionCacheState();

            state.Set(kind, position);
        }

        private void ClearCachedPosition(long entityId, SiNpcCachedPositionKind kind)
        {
            if (entityId == 0)
                return;
            if (!_positionCache.TryGetValue(entityId, out var state) || state == null)
                return;

            state.Clear(kind);
            if (state.IsEmpty)
                _positionCache.Remove(entityId);
        }

        private bool TryFollowCachedPosition(
            SiNpc npc,
            SiNpcCachedPositionKind kind,
            double refreshDistanceSquared)
        {
            if (npc == null || Npcs == null)
                return false;
            if (!TryGetCachedPosition(npc.EntityId, kind, out var target))
                return false;

            var mover = npc as ISiWaypointMover;
            if (mover != null
                && mover.HasWaypoint
                && refreshDistanceSquared > 0
                && Vector3D.DistanceSquared(mover.Waypoint, target) < refreshDistanceSquared)
                return true;

            return Npcs.TrySetWaypoint(npc.EntityId, target);
        }

        private bool TryGetCachedPosition(long entityId, SiNpcCachedPositionKind kind, out Vector3D position)
        {
            position = Vector3D.Zero;
            if (entityId == 0)
                return false;
            if (!_positionCache.TryGetValue(entityId, out var state) || state == null)
                return false;

            return state.TryGet(kind, out position);
        }

        private bool TryGetUnsafeThrowPosition(
            long entityId,
            SiNpc squadmate,
            in Vector3D throwOrigin,
            in Vector3D targetPosition,
            double blastRadius,
            double trajectoryRadius,
            out Vector3D blockingPosition)
        {
            blockingPosition = Vector3D.Zero;

            if (_positionCache.TryGetValue(entityId, out var cacheState) && cacheState != null)
            {
                if (cacheState.TryGet(SiNpcCachedPositionKind.Cover, out var cachedCover)
                    && IsThrowDangerousForPosition(throwOrigin, targetPosition, cachedCover, blastRadius, trajectoryRadius))
                {
                    blockingPosition = cachedCover;
                    return true;
                }

                if (cacheState.TryGet(SiNpcCachedPositionKind.PlainView, out var cachedPlainView)
                    && IsThrowDangerousForPosition(throwOrigin, targetPosition, cachedPlainView, blastRadius, trajectoryRadius))
                {
                    blockingPosition = cachedPlainView;
                    return true;
                }
            }

            var entity = squadmate?.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return false;

            var currentPosition = entity.WorldMatrix.Translation;
            if (!IsThrowDangerousForPosition(throwOrigin, targetPosition, currentPosition, blastRadius, trajectoryRadius))
                return false;

            blockingPosition = currentPosition;
            return true;
        }

        private static bool IsThrowDangerousForPosition(
            in Vector3D throwOrigin,
            in Vector3D targetPosition,
            in Vector3D friendlyPosition,
            double blastRadius,
            double trajectoryRadius)
        {
            if (blastRadius > 0
                && Vector3D.DistanceSquared(friendlyPosition, targetPosition) <= blastRadius * blastRadius)
                return true;

            if (trajectoryRadius <= 0)
                return false;

            return DistanceSquaredToSegment(friendlyPosition, throwOrigin, targetPosition)
                   <= trajectoryRadius * trajectoryRadius;
        }

        private static double DistanceSquaredToSegment(
            in Vector3D point,
            in Vector3D segmentStart,
            in Vector3D segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var segmentLengthSquared = segment.LengthSquared();
            if (segmentLengthSquared <= 0.0001)
                return Vector3D.DistanceSquared(point, segmentStart);

            var t = Vector3D.Dot(point - segmentStart, segment) / segmentLengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            var closestPoint = segmentStart + segment * t;
            return Vector3D.DistanceSquared(point, closestPoint);
        }

        private static SiNpcCachedPositionKind ToCachedPositionKind(SiCombatMovementRole role)
        {
            switch (role)
            {
                case SiCombatMovementRole.Covered:
                    return SiNpcCachedPositionKind.Cover;
                case SiCombatMovementRole.PlainView:
                    return SiNpcCachedPositionKind.PlainView;
                case SiCombatMovementRole.None:
                default:
                    return SiNpcCachedPositionKind.None;
            }
        }

        private static Vector3D FormationOffset(
            SiSquadFormation formation,
            int index,
            int count,
            in Vector3D forward,
            in Vector3D right,
            SiSquadSystemDefinition definition)
        {
            switch (formation)
            {
                case SiSquadFormation.File:
                    return -forward * definition.FollowDistance;
                case SiSquadFormation.Line:
                    var wingRank = index / 2;
                    var wingSide = index % 2 == 0 ? -1 : 1;
                    return -forward * (definition.FollowDistance * 0.35 + wingRank * definition.LineSpacing * 0.5)
                           + right * (wingSide * (definition.LineSpacing + wingRank * definition.LineSpacing));
                case SiSquadFormation.Vee:
                    var row = (index + 2) / 2;
                    var side = index % 2 == 0 ? -1 : 1;
                    return -forward * (definition.FollowDistance + row * definition.VeeSpacing)
                           + right * (side * row * definition.VeeSpacing);
                case SiSquadFormation.LongBox:
                    return FilledBoxFormationOffset(
                        index,
                        count,
                        definition.LongBoxAspectRatio,
                        forward,
                        right,
                        definition);
                case SiSquadFormation.WideBox:
                    return FilledBoxFormationOffset(
                        index,
                        count,
                        definition.WideBoxAspectRatio,
                        forward,
                        right,
                        definition);
                case SiSquadFormation.Square:
                    return FilledBoxFormationOffset(
                        index,
                        count,
                        definition.SquareBoxAspectRatio,
                        forward,
                        right,
                        definition);
                case SiSquadFormation.Column:
                default:
                    return -forward * definition.FollowDistance;
            }
        }

        private static Vector3D FilledBoxFormationOffset(
            int index,
            int count,
            double aspectRatio,
            in Vector3D forward,
            in Vector3D right,
            SiSquadSystemDefinition definition)
        {
            var spacing = definition.FormationBoxSpacing > 0
                ? definition.FormationBoxSpacing
                : definition.FollowDistance;
            var safeCount = Math.Max(1, count);
            var safeRatio = aspectRatio > 0 ? aspectRatio : 1;
            var lengthSlots = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(safeCount * safeRatio)));
            var widthSlots = Math.Max(1, (int)Math.Ceiling(safeCount / (double)lengthSlots));
            var slotIndex = Math.Max(0, index);
            // Fill each lateral row before opening the next depth row. This keeps
            // the box rectangular instead of leaving a partially populated side.
            var row = slotIndex / widthSlots;
            var column = slotIndex % widthSlots;
            var lateralOffset = (column - (widthSlots - 1) * 0.5) * spacing;
            var depthOffset = definition.FollowDistance + row * spacing;
            return -forward * depthOffset + right * lateralOffset;
        }

        private static bool TryGetLeaderTransform(long leaderIdentityId, out MatrixD transform)
        {
            transform = MatrixD.Identity;
            if (MyPlayers.Static == null)
                return false;

            foreach (var entry in MyPlayers.Static.GetAllPlayers())
            {
                var player = entry.Value;
                if (player?.Identity == null || player.Identity.Id != leaderIdentityId)
                    continue;

                var position = player.ControlledEntity?.Get<MyPositionComponentBase>();
                if (position == null)
                    return false;

                transform = position.WorldMatrix;
                return true;
            }

            return false;
        }

        private bool IsPlayerLeaderActive(long leaderIdentityId)
        {
            MatrixD leaderTransform;
            return leaderIdentityId != 0 && TryGetLeaderTransform(leaderIdentityId, out leaderTransform);
        }

        private static void CreateLeaderFrame(
            in MatrixD leaderTransform,
            in Vector3D movementDirection,
            out Vector3D origin,
            out Vector3D forward,
            out Vector3D right)
        {
            origin = leaderTransform.Translation;
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(origin);
            var up = gravity.LengthSquared() > 0.0001
                ? -Vector3D.Normalize(gravity)
                : NormalizedOrFallback(leaderTransform.Up, Vector3D.Up);

            forward = movementDirection.LengthSquared() > 0.0001
                ? Vector3D.Reject(movementDirection, up)
                : Vector3D.Reject(leaderTransform.Forward, up);
            forward = NormalizedOrFallback(forward, Vector3D.CalculatePerpendicularVector(up));
            right = NormalizedOrFallback(Vector3D.Cross(forward, up), leaderTransform.Right);
        }

        private void UpdateTrackedMotionStates()
        {
            foreach (var entry in _squadOrders)
            {
                if (entry.Value.Mode != SiSquadOrderMode.Follow)
                    continue;

                MatrixD leaderTransform;
                if (TryGetLeaderTransform(entry.Key, out leaderTransform))
                    UpdateLeaderMotionState(entry.Key, leaderTransform);
            }

            if (Npcs == null)
                return;

            foreach (var npc in Npcs.Npcs.Values)
            {
                var entity = npc?.Entity;
                if (entity == null || entity.Closed || entity.MarkedForClose)
                    continue;

                UpdateMotionState(
                    _npcMotionStates,
                    npc.EntityId,
                    entity.WorldMatrix.Translation,
                    entity.WorldMatrix.Forward);
            }
        }

        private SiMotionState UpdateLeaderMotionState(long leaderIdentityId, in MatrixD leaderTransform)
        {
            return UpdateMotionState(
                _leaderMotionStates,
                leaderIdentityId,
                leaderTransform.Translation,
                leaderTransform.Forward);
        }

        private SiMotionState UpdateMotionState(
            Dictionary<long, SiMotionState> states,
            long entityId,
            in Vector3D position,
            in Vector3D fallbackForward)
        {
            SiMotionState state;
            if (!states.TryGetValue(entityId, out state))
                states.Add(entityId, state = new SiMotionState());

            var up = SurfaceUp(position);
            if (state.HasPosition)
            {
                var delta = Vector3D.Reject(position - state.Position, up);
                if (delta.LengthSquared() > 0.0025)
                    state.Direction = Vector3D.Normalize(delta);
            }

            if (state.Direction.LengthSquared() <= 0.0001)
            {
                var projectedFallback = Vector3D.Reject(fallbackForward, up);
                if (projectedFallback.LengthSquared() > 0.0001)
                    state.Direction = Vector3D.Normalize(projectedFallback);
            }

            state.Position = position;
            state.HasPosition = true;
            return state;
        }

        private static Vector3D SurfaceUp(in Vector3D position)
        {
            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(position);
            if (gravity.LengthSquared() > 0.0001)
                return -Vector3D.Normalize(gravity);

            return Vector3D.Up;
        }

        private SiFollowAnchor TryGetNpcFollowAnchor(SiNpc npc, in Vector3D fallbackForward)
        {
            var entity = npc?.Entity;
            if (entity == null || entity.Closed || entity.MarkedForClose)
                return new SiFollowAnchor(Vector3D.Zero, fallbackForward);

            var forward = fallbackForward;
            SiMotionState state;
            if (_npcMotionStates.TryGetValue(npc.EntityId, out state)
                && state.Direction.LengthSquared() > 0.0001)
                forward = state.Direction;

            return new SiFollowAnchor(entity.WorldMatrix.Translation, forward);
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > 0.0001
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }

        private static string OrderName(SiSquadOrderMode mode) =>
            mode == SiSquadOrderMode.Follow ? "Follow" : "Stopped";

        private static string TransportModeName(SiSquadTransportMode mode)
        {
            switch (mode)
            {
                case SiSquadTransportMode.Mount:
                    return "Mount";
                case SiSquadTransportMode.Disembark:
                    return "Disembark";
                default:
                    return "None";
            }
        }

        private static string FormationName(SiSquadFormation formation)
        {
            switch (formation)
            {
                case SiSquadFormation.File:
                    return "File";
                case SiSquadFormation.Line:
                    return "Line";
                case SiSquadFormation.Vee:
                    return "Vee";
                case SiSquadFormation.LongBox:
                    return "Long box";
                case SiSquadFormation.WideBox:
                    return "Wide box";
                case SiSquadFormation.Square:
                    return "Square";
                case SiSquadFormation.StaggeredColumn:
                    return "Staggered column";
                case SiSquadFormation.Column:
                default:
                    return "Column";
            }
        }

        private static string EngagementName(SiSquadEngagementStance stance)
        {
            switch (stance)
            {
                case SiSquadEngagementStance.EnemiesNeutrals:
                    return "Enemies and neutrals";
                case SiSquadEngagementStance.HoldFire:
                    return "Hold fire";
                case SiSquadEngagementStance.Enemies:
                default:
                    return "Enemies only";
            }
        }

        private static string CombatStanceName(SiSquadCombatStance stance)
        {
            switch (stance)
            {
                case SiSquadCombatStance.Combat:
                    return "Combat";
                case SiSquadCombatStance.Safe:
                default:
                    return "Safe";
            }
        }

        private static string CombatStanceChangeReport(
            SiSquadCombatStance stance,
            SiSquadCombatTransitionReason reason)
        {
            switch (stance)
            {
                case SiSquadCombatStance.Combat:
                    switch (reason)
                    {
                        case SiSquadCombatTransitionReason.OpeningFire:
                            return "Engaging, combat stance.";
                        case SiSquadCombatTransitionReason.TakingFire:
                            return "Taking fire, combat stance.";
                        case SiSquadCombatTransitionReason.EnemySpotted:
                            return "Contact, combat stance.";
                        case SiSquadCombatTransitionReason.PlayerOrder:
                            return "Combat stance.";
                        default:
                            return "Combat stance.";
                    }
                case SiSquadCombatStance.Safe:
                default:
                    return reason == SiSquadCombatTransitionReason.AreaClear
                        ? "Area clear, safe stance."
                        : "Safe stance.";
            }
        }

        private static string PlayerOrderCombatStanceReport(SiSquadCombatStance stance) =>
            stance == SiSquadCombatStance.Combat
                ? "Switch to combat movement."
                : "Switch to safe movement.";

        private static SiSquadLeaderKey PlayerLeaderKey(long identityId) =>
            new SiSquadLeaderKey(
                SiSquadLeaderKind.Player,
                identityId,
                SiSquadBook.ArmyForPlayerIdentity(identityId));

        private static long CurrentTimeMilliseconds()
        {
            var session = MyAPIGateway.Session;
            return session != null
                ? (long)session.ElapsedPlayTime.TotalMilliseconds
                : 0;
        }

        private static MyPlayer LocalPlayer() =>
            MyAPIGateway.Session?.Player as MyPlayer;

        private static bool CanManageNpcs(ulong sender) =>
            MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.IsAdminModeEnabled(sender);

        private static bool IsAuthoritative =>
            MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer;

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
            return PrepareSpawnTransform(MatrixD.CreateWorld(position, -playerForward, up));
        }

        private static MatrixD CreateSpawnTransform(in MatrixD playerTransform, int squadIndex)
        {
            if (squadIndex <= 0)
                return CreateSpawnTransform(playerTransform);

            var gravity = MyGravityProviderSystem.CalculateTotalGravityInPoint(playerTransform.Translation);
            var up = gravity.LengthSquared() > 0.0001f
                ? -Vector3D.Normalize(gravity)
                : playerTransform.Up;

            var playerForward = Vector3D.Reject(playerTransform.Forward, up);
            if (playerForward.LengthSquared() < 0.0001)
                playerForward = Vector3D.CalculatePerpendicularVector(up);
            playerForward.Normalize();

            var right = Vector3D.Cross(playerForward, up);
            if (right.LengthSquared() < 0.0001)
                right = Vector3D.CalculatePerpendicularVector(playerForward);
            right.Normalize();

            var row = (squadIndex + 1) / 2;
            var side = squadIndex % 2 == 0 ? 1.0 : -1.0;
            var lateralOffset = 1.25 * row * side;
            var depthOffset = 0.9 + (row - 1) * 1.35;
            var position = playerTransform.Translation
                           + playerForward * (SpawnDistance + depthOffset)
                           + right * lateralOffset;
            return PrepareSpawnTransform(MatrixD.CreateWorld(position, -playerForward, up));
        }

        private static MatrixD PrepareSpawnTransform(in MatrixD spawnTransform)
        {
            var preparedTransform = spawnTransform;
            var up = preparedTransform.Up;
            if (up.LengthSquared() < 0.0001 || MyAPIGateway.Physics == null)
                return preparedTransform;

            up.Normalize();
            for (var attempt = 0; attempt < SpawnProbeMaxElevations; attempt++)
            {
                var rayStart = preparedTransform.Translation;
                var rayEnd = rayStart + up * SpawnProbeLength;
                IHitInfo hit;
                if (!MyAPIGateway.Physics.CastRay(rayStart, rayEnd, out hit) || hit == null)
                    break;

                preparedTransform.Translation += up * SpawnProbeElevation;
            }

            return preparedTransform;
        }
    }
}
