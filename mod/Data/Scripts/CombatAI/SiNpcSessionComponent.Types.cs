using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using VRage;
using VRage.Game;
using VRage.Network;
using VRage.ObjectBuilders;
using VRage.ObjectBuilders.Components;
using VRageMath;

namespace Si.UtilityAI
{
    public sealed partial class SiNpcSessionComponent
    {
        private readonly struct SiNpcSpawnRequest
        {
            public SiNpcSpawnRequest(string webbingSubtype, bool isParatrooper, bool isEnemy)
            {
                WebbingSubtype = string.IsNullOrWhiteSpace(webbingSubtype)
                    ? null
                    : webbingSubtype.Trim();
                IsParatrooper = isParatrooper;
                IsEnemy = isEnemy;
            }

            public string WebbingSubtype { get; }
            public bool IsParatrooper { get; }
            public bool IsEnemy { get; }
            public string DisplayArchetype =>
                string.IsNullOrWhiteSpace(WebbingSubtype)
                    ? "trooper"
                    : WebbingSubtype
                      + (IsParatrooper ? "-paratrooper" : string.Empty)
                      + (IsEnemy ? "-enemy" : string.Empty);
        }

        [RpcSerializable]
        private struct SiNpcSnapshot
        {
            public long EntityId;
            public string Archetype;
            public string WebbingSubtype;
            public bool IsParatrooper;
            public bool IsEnemy;
            public MatrixD Transform;
            public bool HasWaypoint;
            public Vector3D Waypoint;
            public bool HasSquadAssignment;
            public byte SquadLeaderKind;
            public long SquadLeaderId;
            public byte SquadArmyKind;
            public long SquadArmyId;
            public bool IsSquadLeader;
            public string LeaderName;
        }
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcSessionComponent : MyObjectBuilder_SessionComponent
    {
        [XmlElement("Npc")]
        public List<SavedNpc> Npcs;

        [XmlElement("SquadOrder")]
        public List<SquadOrder> SquadOrders;

        public class SavedNpc
        {
            [XmlAttribute]
            public long EntityId;

            [XmlAttribute]
            public string Archetype;

            [XmlAttribute]
            public string WebbingSubtype;

            [XmlAttribute]
            public bool IsParatrooper;

            [XmlAttribute]
            public bool IsEnemy;

            public MyPositionAndOrientation Transform;

            public bool HasWaypoint;

            public SerializableVector3D Waypoint;

            public bool HasSquadAssignment;

            [XmlAttribute]
            public byte SquadLeaderKind;

            [XmlAttribute]
            public long SquadLeaderId;

            [XmlAttribute]
            public byte SquadArmyKind;

            [XmlAttribute]
            public long SquadArmyId;

            [XmlAttribute]
            public bool IsSquadLeader;

            [XmlAttribute]
            public string LeaderName;

            [XmlAttribute]
            public long DiplomaticIdentityId;

            [XmlAttribute]
            public bool HasTransportState;

            [XmlAttribute]
            public long TransportVehicleEntityId;

            [XmlAttribute]
            public long SeatEntityId;

            [XmlAttribute]
            public string SeatSlotName;

            public bool HasTransportExitLocalPosition;

            public SerializableVector3D TransportExitLocalPosition;

            [XmlAttribute]
            public bool WasInTransportSeat;
        }

        public class SquadOrder
        {
            [XmlAttribute]
            public long LeaderIdentityId;

            [XmlAttribute]
            public byte Mode;

            [XmlAttribute]
            public byte Formation;

            [XmlAttribute]
            public byte EngagementStance;

            [XmlAttribute]
            public byte TransportMode;

            [XmlAttribute]
            public long TransportVehicleEntityId;

            [XmlAttribute]
            public byte CombatStance;
        }
    }

    internal enum SiSquadOrderMode
    {
        Stopped,
        Follow,
    }

    internal enum SiSquadTransportMode
    {
        None,
        Mount,
        Disembark,
    }

    internal enum SiSquadFormation
    {
        Column,
        File,
        Line,
        Vee,
    }

    internal enum SiSquadEngagementStance
    {
        Enemies,
        EnemiesNeutrals,
        HoldFire,
    }

    internal enum SiSquadCombatStance
    {
        Safe,
        Combat,
    }

    internal enum SiSquadCombatTransitionReason
    {
        PlayerOrder,
        OpeningFire,
        EnemySpotted,
        TakingFire,
        AreaClear,
    }

    internal sealed class SiSquadCommandState
    {
        public SiSquadOrderMode Mode { get; set; }
        public SiSquadFormation Formation { get; set; }
        public SiSquadEngagementStance EngagementStance { get; set; }
        public SiSquadTransportMode TransportMode { get; set; }
        public long TransportVehicleEntityId { get; set; }
        public SiSquadTransportMode TransportCadenceMode { get; set; }
        public long NextTransportActionTimeMilliseconds { get; set; }
    }

    internal sealed class SiSquadCombatState
    {
        public string LeaderName { get; set; }
        public SiSquadCombatStance Stance { get; set; }
        public long LastShotAtTime { get; set; }
        public long LastEnemySpottedTime { get; set; }
        public long LastStanceChangeTime { get; set; }
        public long CombatEntryToken { get; set; }
    }

    internal sealed class SiAiSquadMoveOrderState
    {
        public SiAiSquadMoveOrderState(in Vector3D target)
        {
            Target = target;
        }

        public Vector3D Target { get; set; }
    }

    internal sealed class SiPlayerLeaderState
    {
        public bool WasActive { get; set; }
    }

    internal sealed class SiMotionState
    {
        public bool HasPosition { get; set; }
        public Vector3D Position { get; set; }
        public Vector3D Direction { get; set; }
    }

    internal sealed class SiTransportNpcState
    {
        public long VehicleEntityId { get; set; }
        public long SeatEntityId { get; set; }
        public string SeatSlotName { get; set; }
        public bool HasExitLocalPosition { get; set; }
        public Vector3D ExitLocalPosition { get; set; }
    }

    internal struct SiCoverReservation
    {
        public SiCoverReservation(in Vector3D position, double radius)
        {
            Position = position;
            Radius = radius;
        }

        public Vector3D Position { get; }
        public double Radius { get; }
    }

    internal sealed class SiCoverSearchCacheEntry
    {
        public long ExpiresAtMilliseconds;
        public readonly List<SiCoverSearchCandidate> Candidates = new List<SiCoverSearchCandidate>();
        public int ScannedSectors;
        public int IntersectingSectors;
        public int FoliageEntries;
        public int CandidateCount;
        public int StandingRejects;
        public int ViableCount;
    }

    internal sealed class SiCoverScanCacheEntry
    {
        public long ExpiresAtMilliseconds;
        public readonly List<Vector3D> CoverPositions = new List<Vector3D>();
        public int ScannedSectors;
        public int IntersectingSectors;
        public int FoliageEntries;
        public int CandidateCount;
    }

    internal struct SiCoverSearchCandidate
    {
        public SiCoverSearchCandidate(
            in Vector3D coverPosition,
            in Vector3D standPosition,
            bool isTree,
            double distanceSquared)
        {
            CoverPosition = coverPosition;
            StandPosition = standPosition;
            IsTree = isTree;
            DistanceSquared = distanceSquared;
        }

        public Vector3D CoverPosition { get; }
        public Vector3D StandPosition { get; }
        public bool IsTree { get; }
        public double DistanceSquared { get; }
    }

    internal struct SiCoverSearchCacheKey : IEquatable<SiCoverSearchCacheKey>
    {
        private const double ThreatDirectionQuantization = 0.35;
        private readonly int _originX;
        private readonly int _originY;
        private readonly int _originZ;
        private readonly int _radius;
        private readonly int _directionX;
        private readonly int _directionY;
        private readonly int _directionZ;
        private readonly MyDefinitionId _behaviorDefinitionId;

        public SiCoverSearchCacheKey(
            in Vector3D searchOrigin,
            double searchRadius,
            in Vector3D threatPosition,
            long threatEntityId,
            MyDefinitionId behaviorDefinitionId,
            double quantization)
        {
            _originX = Quantize(searchOrigin.X, quantization);
            _originY = Quantize(searchOrigin.Y, quantization);
            _originZ = Quantize(searchOrigin.Z, quantization);
            _radius = Quantize(searchRadius, 0.25);
            var direction = ResolveThreatDirection(searchOrigin, threatPosition, threatEntityId);
            _directionX = Quantize(direction.X, ThreatDirectionQuantization);
            _directionY = Quantize(direction.Y, ThreatDirectionQuantization);
            _directionZ = Quantize(direction.Z, ThreatDirectionQuantization);
            _behaviorDefinitionId = behaviorDefinitionId;
        }

        public bool Equals(SiCoverSearchCacheKey other)
        {
            return _originX == other._originX
                   && _originY == other._originY
                   && _originZ == other._originZ
                   && _radius == other._radius
                   && _directionX == other._directionX
                   && _directionY == other._directionY
                   && _directionZ == other._directionZ
                   && _behaviorDefinitionId.Equals(other._behaviorDefinitionId);
        }

        public override bool Equals(object obj)
        {
            return obj is SiCoverSearchCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _originX;
                hash = hash * 31 + _originY;
                hash = hash * 31 + _originZ;
                hash = hash * 31 + _radius;
                hash = hash * 31 + _directionX;
                hash = hash * 31 + _directionY;
                hash = hash * 31 + _directionZ;
                hash = hash * 31 + _behaviorDefinitionId.GetHashCode();
                return hash;
            }
        }

        private static Vector3D ResolveThreatDirection(
            in Vector3D searchOrigin,
            in Vector3D threatPosition,
            long threatEntityId)
        {
            var delta = threatPosition - searchOrigin;
            if (delta.LengthSquared() > 0.0001)
                return Vector3D.Normalize(delta);

            if (threatEntityId != 0)
                return Vector3D.Forward;

            return Vector3D.Zero;
        }

        private static int Quantize(double value, double step)
        {
            if (step <= 0)
                return 0;

            return (int)Math.Round(value / step);
        }
    }

    internal struct SiCoverScanCacheKey : IEquatable<SiCoverScanCacheKey>
    {
        private readonly int _originX;
        private readonly int _originY;
        private readonly int _originZ;
        private readonly int _radius;
        private readonly MyDefinitionId _behaviorDefinitionId;

        public SiCoverScanCacheKey(
            in Vector3D searchOrigin,
            double searchRadius,
            MyDefinitionId behaviorDefinitionId,
            double quantization)
        {
            _originX = Quantize(searchOrigin.X, quantization);
            _originY = Quantize(searchOrigin.Y, quantization);
            _originZ = Quantize(searchOrigin.Z, quantization);
            _radius = Quantize(searchRadius, 0.25);
            _behaviorDefinitionId = behaviorDefinitionId;
        }

        public bool Equals(SiCoverScanCacheKey other)
        {
            return _originX == other._originX
                   && _originY == other._originY
                   && _originZ == other._originZ
                   && _radius == other._radius
                   && _behaviorDefinitionId.Equals(other._behaviorDefinitionId);
        }

        public override bool Equals(object obj)
        {
            return obj is SiCoverScanCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + _originX;
                hash = hash * 31 + _originY;
                hash = hash * 31 + _originZ;
                hash = hash * 31 + _radius;
                hash = hash * 31 + _behaviorDefinitionId.GetHashCode();
                return hash;
            }
        }

        private static int Quantize(double value, double step)
        {
            if (step <= 0)
                return 0;

            return (int)Math.Round(value / step);
        }
    }

    internal struct SiFollowAnchor
    {
        public SiFollowAnchor(in Vector3D position, in Vector3D forward)
        {
            Position = position;
            Forward = forward;
        }

        public Vector3D Position { get; }
        public Vector3D Forward { get; }
    }

    internal enum SiNpcCachedPositionKind
    {
        None,
        Formation,
        Cover,
        PlainView,
    }

    internal sealed class SiNpcPositionCacheState
    {
        public bool HasFormation { get; private set; }
        public bool HasCover { get; private set; }
        public bool HasPlainView { get; private set; }
        public Vector3D FormationPosition { get; private set; }
        public Vector3D CoverPosition { get; private set; }
        public Vector3D PlainViewPosition { get; private set; }

        public bool IsEmpty => !HasFormation && !HasCover && !HasPlainView;

        public void Set(SiNpcCachedPositionKind kind, in Vector3D position)
        {
            switch (kind)
            {
                case SiNpcCachedPositionKind.Formation:
                    FormationPosition = position;
                    HasFormation = true;
                    return;
                case SiNpcCachedPositionKind.Cover:
                    CoverPosition = position;
                    HasCover = true;
                    return;
                case SiNpcCachedPositionKind.PlainView:
                    PlainViewPosition = position;
                    HasPlainView = true;
                    return;
                case SiNpcCachedPositionKind.None:
                default:
                    return;
            }
        }

        public void Clear(SiNpcCachedPositionKind kind)
        {
            switch (kind)
            {
                case SiNpcCachedPositionKind.Formation:
                    HasFormation = false;
                    FormationPosition = Vector3D.Zero;
                    return;
                case SiNpcCachedPositionKind.Cover:
                    HasCover = false;
                    CoverPosition = Vector3D.Zero;
                    return;
                case SiNpcCachedPositionKind.PlainView:
                    HasPlainView = false;
                    PlainViewPosition = Vector3D.Zero;
                    return;
                case SiNpcCachedPositionKind.None:
                default:
                    return;
            }
        }

        public bool TryGet(SiNpcCachedPositionKind kind, out Vector3D position)
        {
            position = Vector3D.Zero;
            switch (kind)
            {
                case SiNpcCachedPositionKind.Formation:
                    position = FormationPosition;
                    return HasFormation;
                case SiNpcCachedPositionKind.Cover:
                    position = CoverPosition;
                    return HasCover;
                case SiNpcCachedPositionKind.PlainView:
                    position = PlainViewPosition;
                    return HasPlainView;
                case SiNpcCachedPositionKind.None:
                default:
                    return false;
            }
        }
    }
}
