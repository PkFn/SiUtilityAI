using System;
using System.Xml.Serialization;
using Pax.Cannons;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiShootOpposingNpcBehavior : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float SearchRadius;
        public float BaseScore;
        public float DistanceScore;
        public float DistanceExponent;

        public int FireCooldownMilliseconds;
        public string Projectile;
        public float ProjectileVelocityMultiplier;
        public float ProjectileAccuracyMultiplier;
        public float ProjectileSyncDistance;
        public float CharacterDamageMultiplier;

        public float AimTargetHeight;
        public float AimExtraHeight;
        public float AimCloseRangeDistance;
        public float AimCloseRangeHeightOffset;
        public float ExpectedProjectileVelocity;
        public float ElevationAiming;
        public float MuzzleForwardOffset;
        public float MuzzleUpOffset;

        public bool RequireLineOfSight;
        public bool RotateToTarget;

        [XmlArrayItem("Archetype")]
        public string[] TargetArchetypes;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition))]
    public class SiShootOpposingNpcBehaviorDefinition : MyEntityComponentDefinition
    {
        public float SearchRadius { get; private set; }
        public float BaseScore { get; private set; }
        public float DistanceScore { get; private set; }
        public float DistanceExponent { get; private set; }

        public int FireCooldownMilliseconds { get; private set; }
        public string Projectile { get; private set; }
        public float ProjectileVelocityMultiplier { get; private set; }
        public float ProjectileAccuracyMultiplier { get; private set; }
        public float ProjectileSyncDistance { get; private set; }
        public float CharacterDamageMultiplier { get; private set; }

        public float AimTargetHeight { get; private set; }
        public float AimExtraHeight { get; private set; }
        public float AimCloseRangeDistance { get; private set; }
        public float AimCloseRangeHeightOffset { get; private set; }
        public float ExpectedProjectileVelocity { get; private set; }
        public float ElevationAiming { get; private set; }
        public float MuzzleForwardOffset { get; private set; }
        public float MuzzleUpOffset { get; private set; }

        public bool RequireLineOfSight { get; private set; }
        public bool RotateToTarget { get; private set; }
        public string[] TargetArchetypes { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiShootOpposingNpcBehaviorDefinition)builder;

            SearchRadius = Math.Max(0, ob.SearchRadius);
            BaseScore = Math.Max(0, ob.BaseScore);
            DistanceScore = Math.Max(0, ob.DistanceScore);
            DistanceExponent = Math.Max(0.01f, ob.DistanceExponent);

            FireCooldownMilliseconds = Math.Max(1, ob.FireCooldownMilliseconds);
            Projectile = ob.Projectile;
            ProjectileVelocityMultiplier = Math.Max(0, ob.ProjectileVelocityMultiplier);
            ProjectileAccuracyMultiplier = Math.Max(0, ob.ProjectileAccuracyMultiplier);
            ProjectileSyncDistance = Math.Max(0, ob.ProjectileSyncDistance);
            CharacterDamageMultiplier = Math.Max(0, ob.CharacterDamageMultiplier);

            AimTargetHeight = Math.Max(0, ob.AimTargetHeight);
            AimExtraHeight = ob.AimExtraHeight;
            AimCloseRangeDistance = Math.Max(0, ob.AimCloseRangeDistance);
            AimCloseRangeHeightOffset = ob.AimCloseRangeHeightOffset;
            ExpectedProjectileVelocity = Math.Max(0.01f, ob.ExpectedProjectileVelocity);
            ElevationAiming = Math.Max(0.01f, ob.ElevationAiming);
            MuzzleForwardOffset = ob.MuzzleForwardOffset;
            MuzzleUpOffset = ob.MuzzleUpOffset;

            RequireLineOfSight = ob.RequireLineOfSight;
            RotateToTarget = ob.RotateToTarget;
            TargetArchetypes = ob.TargetArchetypes ?? EmptyArchetypes;
        }

        private static readonly string[] EmptyArchetypes = new string[0];
    }

    /// <summary>
    /// Scores opposing NPCs and fires PAX defender rifle projectiles at the
    /// selected target.  Weapon tuning is supplied by the attached definition.
    /// </summary>
    [MyComponent(typeof(MyObjectBuilder_SiShootOpposingNpcBehavior))]
    [MyDefinitionRequired(typeof(SiShootOpposingNpcBehaviorDefinition))]
    public class SiShootOpposingNpcBehaviorComponent : MyEntityComponent, ISiUtilityBehavior
    {
        private SiShootOpposingNpcBehaviorDefinition _definition;
        private SiNpc _target;
        private long _fireCooldown;

        public string BehaviorName => DefinitionId.ToString();

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiShootOpposingNpcBehaviorDefinition)definition;
        }

        float ISiUtilityBehavior.Evaluate(SiUtilityContext context)
        {
            if (!CanShoot)
            {
                _target = null;
                return 0;
            }

            var target = FindBestTarget(context, out var distance);
            _target = target;
            if (target == null)
                return 0;

            var normalizedDistance = _definition.SearchRadius > 0
                ? MathHelper.Clamp(1f - (float)(distance / _definition.SearchRadius), 0, 1)
                : 1;
            return _definition.BaseScore
                   + _definition.DistanceScore
                   * (float)Math.Pow(normalizedDistance, _definition.DistanceExponent);
        }

        void ISiUtilityBehavior.Begin(SiUtilityContext context)
        {
            _fireCooldown = 0;
        }

        void ISiUtilityBehavior.Tick(SiUtilityContext context, long elapsedMilliseconds)
        {
            if (!CanShoot || !IsValidTarget(context.Agent, _target))
                return;

            var targetEntity = _target.Entity;
            if (_definition.RotateToTarget)
                FaceTarget(context.Entity, targetEntity);

            _fireCooldown -= elapsedMilliseconds;
            if (_fireCooldown > 0)
                return;

            if (!TryCreateShot(context, _target, out var projectileMatrix))
                return;

            if (SiPaxProjectileSpawner.TryCreateSyncedProjectile(
                    _definition.Projectile,
                    projectileMatrix,
                    _definition.ProjectileVelocityMultiplier,
                    _definition.ProjectileAccuracyMultiplier,
                    Vector3.Zero,
                    _definition.ProjectileSyncDistance,
                    _definition.CharacterDamageMultiplier,
                    context.EntityId))
                _fireCooldown = _definition.FireCooldownMilliseconds;
        }

        void ISiUtilityBehavior.End(SiUtilityContext context)
        {
            _target = null;
            _fireCooldown = 0;
        }

        private bool CanShoot =>
            !string.IsNullOrWhiteSpace(_definition.Projectile)
            && _definition.SearchRadius > 0
            && _definition.ProjectileVelocityMultiplier > 0
            && _definition.ProjectileAccuracyMultiplier > 0
            && _definition.ProjectileSyncDistance > 0
            && SiPaxProjectileSpawner.IsAvailable
            && ProjectileDefinitionExists(_definition.Projectile);

        private SiNpc FindBestTarget(SiUtilityContext context, out double bestDistance)
        {
            bestDistance = 0;
            var session = SiNpcSessionComponent.Instance;
            var manager = session?.Npcs;
            if (manager == null)
                return null;

            SiNpc best = null;
            var bestDistanceSquared = (double)_definition.SearchRadius * _definition.SearchRadius;
            foreach (var candidate in manager.Npcs.Values)
            {
                if (!IsValidTarget(context.Agent, candidate))
                    continue;
                if (!IsOpposing(context.Agent, candidate, session.Squads))
                    continue;
                if (!CanTargetArchetype(candidate.Archetype))
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(
                    context.Position,
                    candidate.Entity.WorldMatrix.Translation);
                if (distanceSquared > bestDistanceSquared)
                    continue;
                if (_definition.RequireLineOfSight
                    && !HasLineOfSight(context.Entity, candidate.Entity))
                    continue;

                best = candidate;
                bestDistanceSquared = distanceSquared;
            }

            bestDistance = best != null ? Math.Sqrt(bestDistanceSquared) : 0;
            return best;
        }

        private bool TryCreateShot(
            SiUtilityContext context,
            SiNpc target,
            out MatrixD projectileMatrix)
        {
            projectileMatrix = MatrixD.Identity;
            var shooter = context.Entity;
            var targetEntity = target?.Entity;
            if (shooter == null || targetEntity == null)
                return false;
            if (_definition.RequireLineOfSight && !HasLineOfSight(shooter, targetEntity))
                return false;

            var shooterWorld = shooter.WorldMatrix;
            var shooterUp = NormalizedOrFallback(shooterWorld.Up, Vector3D.Up);
            var targetWorld = targetEntity.WorldMatrix;
            var targetUp = NormalizedOrFallback(targetWorld.Up, shooterUp);

            var initialMuzzle = shooterWorld.Translation + shooterUp * _definition.AimTargetHeight;
            var aimPoint = targetWorld.Translation + targetUp * _definition.AimTargetHeight;
            var distance = (initialMuzzle - aimPoint).Length();

            var closeRangeOffset = distance < _definition.AimCloseRangeDistance
                ? _definition.AimCloseRangeHeightOffset
                : 0;
            aimPoint += targetUp * (_definition.AimExtraHeight
                                    + closeRangeOffset
                                    + distance * distance / _definition.ElevationAiming);
            aimPoint += TargetVelocity(target) * (distance / _definition.ExpectedProjectileVelocity);

            var shotDirection = NormalizedOrFallback(aimPoint - initialMuzzle, shooterWorld.Forward);
            var muzzlePosition = shooterWorld.Translation
                                 + shotDirection * _definition.MuzzleForwardOffset
                                 + shooterUp * _definition.MuzzleUpOffset;
            var shotUp = RejectOrFallback(shooterUp, shotDirection, Vector3D.CalculatePerpendicularVector(shotDirection));
            projectileMatrix = MatrixD.CreateWorld(muzzlePosition, shotDirection, shotUp);
            return true;
        }

        private void FaceTarget(MyEntity shooter, MyEntity target)
        {
            if (shooter == null || target == null)
                return;

            var world = shooter.WorldMatrix;
            var up = NormalizedOrFallback(world.Up, Vector3D.Up);
            var toTarget = Vector3D.Reject(target.WorldMatrix.Translation - world.Translation, up);
            if (toTarget.LengthSquared() <= 0.0001)
                return;

            var forward = toTarget / Math.Sqrt(toTarget.LengthSquared());
            shooter.WorldMatrix = MatrixD.CreateWorld(world.Translation, forward, up);
        }

        private bool HasLineOfSight(MyEntity shooter, MyEntity target)
        {
            if (shooter == null || target == null)
                return false;

            var shooterUp = NormalizedOrFallback(shooter.WorldMatrix.Up, Vector3D.Up);
            var targetUp = NormalizedOrFallback(target.WorldMatrix.Up, shooterUp);
            var start = shooter.WorldMatrix.Translation + shooterUp * _definition.AimTargetHeight;
            var end = target.WorldMatrix.Translation + targetUp * _definition.AimTargetHeight;

            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(start, end, out hit))
                return true;

            return hit == null
                   || hit.HitEntity == null
                   || hit.HitEntity == target
                   || hit.HitEntity == shooter;
        }

        private bool IsOpposing(SiNpc self, SiNpc candidate, SiSquadBook squads)
        {
            if (squads != null
                && squads.TryGetAssignment(self.EntityId, out var selfAssignment)
                && squads.TryGetAssignment(candidate.EntityId, out var candidateAssignment))
                return !selfAssignment.Leader.Army.Equals(candidateAssignment.Leader.Army);

            return !string.Equals(self.Archetype, candidate.Archetype, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanTargetArchetype(string archetype)
        {
            if (_definition.TargetArchetypes.Length == 0)
                return true;

            for (var i = 0; i < _definition.TargetArchetypes.Length; i++)
                if (string.Equals(_definition.TargetArchetypes[i], archetype, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsValidTarget(SiNpc self, SiNpc candidate)
        {
            if (self == null || candidate == null || ReferenceEquals(self, candidate))
                return false;

            var entity = candidate.Entity;
            return entity != null && entity.InScene && !entity.Closed && !entity.MarkedForClose;
        }

        private static Vector3D TargetVelocity(SiNpc target)
        {
            if (target is SiGroundedNpc grounded)
                return grounded.Velocity;
            return target?.Entity?.Physics != null
                ? target.Entity.Physics.LinearVelocity
                : Vector3D.Zero;
        }

        private static Vector3D NormalizedOrFallback(in Vector3D value, in Vector3D fallback)
        {
            var lengthSquared = value.LengthSquared();
            return lengthSquared > 0.0001
                ? value / Math.Sqrt(lengthSquared)
                : fallback;
        }

        private static Vector3D RejectOrFallback(
            in Vector3D value,
            in Vector3D direction,
            in Vector3D fallback)
        {
            var rejected = Vector3D.Reject(value, direction);
            return NormalizedOrFallback(rejected, fallback);
        }

        private static bool ProjectileDefinitionExists(string subtype)
        {
            MyContainerDefinition ignored;
            return MyDefinitionManager.TryGet(
                new MyDefinitionId(typeof(MyObjectBuilder_EntityBase), subtype),
                out ignored);
        }
    }

    internal static class SiPaxProjectileSpawner
    {
        public static bool IsAvailable => true;

        public static bool TryCreateSyncedProjectile(
            string projectile,
            MatrixD matrix,
            float velocity,
            float accuracy,
            Vector3 gridVelocity,
            float maxDistance,
            float characterDamageMultiplier,
            long ownerId)
        {
            try
            {
                PAX_Projectile_Spawner.ServerCreateSyncedProjectile(
                    projectile,
                    matrix,
                    velocity,
                    accuracy,
                    gridVelocity,
                    maxDistance,
                    characterDamageMultiplier,
                    ownerId);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
