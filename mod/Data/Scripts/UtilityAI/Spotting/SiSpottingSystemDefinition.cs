using System;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiSpottingSystemDefinition : MyObjectBuilder_DefinitionBase
    {
        public SerializableDefinitionId? VehicleTargeting;
        [DefaultValue(1f)]
        public float Constant = 1f;
        public float CombatThreatDirectionThresholdMultiplier;
        public float CombatThreatDirectionAngleDegrees;
        [DefaultValue(250)]
        public int SpottingReevaluationIntervalMilliseconds = 250;
        [DefaultValue(2000)]
        public int SpottingTrackingTimeoutMilliseconds = 2000;
        [DefaultValue(4f)]
        public float HearingGuaranteedRadius = 4f;
        [DefaultValue(0.4f)]
        public float StillnessVelocityThreshold = 0.4f;
        [DefaultValue(0.75f)]
        public float StillnessChanceMultiplier = 0.75f;
        [DefaultValue(1800)]
        public int RecentShotMilliseconds = 1800;
        [DefaultValue(0.55f)]
        public float NotFiringChanceMultiplier = 0.55f;
        [DefaultValue(0.28f)]
        public float ShotAwarenessPerShot = 0.28f;
        [DefaultValue(0.22f)]
        public float ShotAwarenessDecayPerSecond = 0.22f;
        [DefaultValue(140f)]
        public float ShotAwarenessMaxDistance = 140f;
        [DefaultValue(1.2f)]
        public float ShotAwarenessDistanceExponent = 1.2f;
        [DefaultValue(3.5f)]
        public float NearbyBushScanRadius = 3.5f;
        [DefaultValue(0.45f)]
        public float NearbyBushMinimumChanceMultiplier = 0.45f;
        [DefaultValue(1.5f)]
        public float NearbyBushDistanceExponent = 1.5f;
        [DefaultValue(0.35f)]
        public float DarknessMinimumChanceMultiplier = 0.35f;
        [DefaultValue(-0.2f)]
        public float DarknessNightSolarElevation = -0.2f;
        [DefaultValue(0.2f)]
        public float DarknessDaySolarElevation = 0.2f;
        [DefaultValue(0.35f)]
        public float InteriorChanceMultiplier = 0.35f;
        [DefaultValue(1.5f)]
        public float VehicleSpottingBaseGain = 1.5f;
        [DefaultValue(3f)]
        public float VehicleSpottingMovingSpeedThreshold = 3f;
        [DefaultValue(18f)]
        public float VehicleSpottingMaxSpeed = 18f;
        [DefaultValue(2f)]
        public float VehicleSpottingMovingGain = 2f;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiSpottingSystemDefinition))]
    public class SiSpottingSystemDefinition : MyDefinitionBase
    {
        public MyDefinitionId? VehicleTargetingDefinitionId { get; private set; }
        public float Constant { get; private set; }
        public float CombatThreatDirectionThresholdMultiplier { get; private set; }
        public float CombatThreatDirectionAngleDegrees { get; private set; }
        public int SpottingReevaluationIntervalMilliseconds { get; private set; }
        public int SpottingTrackingTimeoutMilliseconds { get; private set; }
        public float HearingGuaranteedRadius { get; private set; }
        public float StillnessVelocityThreshold { get; private set; }
        public float StillnessChanceMultiplier { get; private set; }
        public int RecentShotMilliseconds { get; private set; }
        public float NotFiringChanceMultiplier { get; private set; }
        public float ShotAwarenessPerShot { get; private set; }
        public float ShotAwarenessDecayPerSecond { get; private set; }
        public float ShotAwarenessMaxDistance { get; private set; }
        public float ShotAwarenessDistanceExponent { get; private set; }
        public float NearbyBushScanRadius { get; private set; }
        public float NearbyBushMinimumChanceMultiplier { get; private set; }
        public float NearbyBushDistanceExponent { get; private set; }
        public float DarknessMinimumChanceMultiplier { get; private set; }
        public float DarknessNightSolarElevation { get; private set; }
        public float DarknessDaySolarElevation { get; private set; }
        public float InteriorChanceMultiplier { get; private set; }
        public float VehicleSpottingBaseGain { get; private set; }
        public float VehicleSpottingMovingSpeedThreshold { get; private set; }
        public float VehicleSpottingMaxSpeed { get; private set; }
        public float VehicleSpottingMovingGain { get; private set; }

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiSpottingSystemDefinition)builder;
            VehicleTargetingDefinitionId = ob.VehicleTargeting.HasValue
                ? (MyDefinitionId?)ob.VehicleTargeting.Value
                : null;
            Constant = Math.Max(0, ob.Constant);
            CombatThreatDirectionThresholdMultiplier = MathHelper.Clamp(
                ob.CombatThreatDirectionThresholdMultiplier,
                0,
                1);
            CombatThreatDirectionAngleDegrees = MathHelper.Clamp(
                ob.CombatThreatDirectionAngleDegrees,
                0,
                180);
            SpottingReevaluationIntervalMilliseconds = Math.Max(50, ob.SpottingReevaluationIntervalMilliseconds);
            SpottingTrackingTimeoutMilliseconds = Math.Max(SpottingReevaluationIntervalMilliseconds, ob.SpottingTrackingTimeoutMilliseconds);
            HearingGuaranteedRadius = Math.Max(0, ob.HearingGuaranteedRadius);
            StillnessVelocityThreshold = Math.Max(0, ob.StillnessVelocityThreshold);
            StillnessChanceMultiplier = MathHelper.Clamp(ob.StillnessChanceMultiplier, 0, 1);
            RecentShotMilliseconds = Math.Max(0, ob.RecentShotMilliseconds);
            NotFiringChanceMultiplier = MathHelper.Clamp(ob.NotFiringChanceMultiplier, 0, 1);
            ShotAwarenessPerShot = MathHelper.Clamp(ob.ShotAwarenessPerShot, 0, 1);
            ShotAwarenessDecayPerSecond = Math.Max(0, ob.ShotAwarenessDecayPerSecond);
            ShotAwarenessMaxDistance = Math.Max(0, ob.ShotAwarenessMaxDistance);
            ShotAwarenessDistanceExponent = Math.Max(0.01f, ob.ShotAwarenessDistanceExponent);
            NearbyBushScanRadius = Math.Max(0, ob.NearbyBushScanRadius);
            NearbyBushMinimumChanceMultiplier = MathHelper.Clamp(ob.NearbyBushMinimumChanceMultiplier, 0, 1);
            NearbyBushDistanceExponent = Math.Max(0.01f, ob.NearbyBushDistanceExponent);
            DarknessMinimumChanceMultiplier = MathHelper.Clamp(ob.DarknessMinimumChanceMultiplier, 0, 1);
            DarknessNightSolarElevation = ob.DarknessNightSolarElevation;
            DarknessDaySolarElevation = ob.DarknessDaySolarElevation;
            InteriorChanceMultiplier = MathHelper.Clamp(ob.InteriorChanceMultiplier, 0, 1);
            VehicleSpottingBaseGain = Math.Max(0, ob.VehicleSpottingBaseGain);
            VehicleSpottingMovingSpeedThreshold = Math.Max(0, ob.VehicleSpottingMovingSpeedThreshold);
            VehicleSpottingMaxSpeed = Math.Max(VehicleSpottingMovingSpeedThreshold, ob.VehicleSpottingMaxSpeed);
            VehicleSpottingMovingGain = Math.Max(0, ob.VehicleSpottingMovingGain);
        }
    }
}
