using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.ObjectBuilders;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiVehicleTargetingSpotScoreDefinition : MyObjectBuilder_DefinitionBase
    {
        [DefaultValue(1)]
        public int DriverPriority = 1;
        [DefaultValue(1)]
        public int DefaultEnginePriority = 1;
        [DefaultValue(0.70f)]
        public float SteamEngineDisabledIntegrityRatio = 0.70f;
        [DefaultValue(0.85f)]
        public float CombustionEngineDisabledIntegrityRatio = 0.85f;
        [DefaultValue(0.95f)]
        public float MechanicalEngineDisabledIntegrityRatio = 0.95f;

        [XmlArrayItem("Engine")]
        public List<EngineOverride> EngineOverrides;

        public class EngineOverride
        {
            [XmlAttribute]
            public string BlockSubtype;

            [DefaultValue(1)]
            [XmlAttribute]
            public int Priority = 1;

            [DefaultValue(-1f)]
            [XmlAttribute]
            public float DisabledIntegrityRatio = -1f;
        }
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiVehicleTargetingSpotScoreDefinition))]
    public class SiVehicleTargetingSpotScoreDefinition : MyDefinitionBase
    {
        private readonly Dictionary<string, EngineOverrideDefinition> _engineOverrides =
            new Dictionary<string, EngineOverrideDefinition>(StringComparer.OrdinalIgnoreCase);

        public int DriverPriority { get; private set; }
        public int DefaultEnginePriority { get; private set; }
        public float SteamEngineDisabledIntegrityRatio { get; private set; }
        public float CombustionEngineDisabledIntegrityRatio { get; private set; }
        public float MechanicalEngineDisabledIntegrityRatio { get; private set; }

        public IEnumerable<EngineOverrideDefinition> EngineOverrides => _engineOverrides.Values;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiVehicleTargetingSpotScoreDefinition)builder;

            DriverPriority = Math.Max(1, ob.DriverPriority);
            DefaultEnginePriority = Math.Max(1, ob.DefaultEnginePriority);
            SteamEngineDisabledIntegrityRatio = ClampIntegrityRatio(ob.SteamEngineDisabledIntegrityRatio);
            CombustionEngineDisabledIntegrityRatio = ClampIntegrityRatio(ob.CombustionEngineDisabledIntegrityRatio);
            MechanicalEngineDisabledIntegrityRatio = ClampIntegrityRatio(ob.MechanicalEngineDisabledIntegrityRatio);

            _engineOverrides.Clear();
            if (ob.EngineOverrides == null)
                return;

            for (var i = 0; i < ob.EngineOverrides.Count; i++)
            {
                var entry = ob.EngineOverrides[i];
                var blockSubtype = entry?.BlockSubtype?.Trim();
                if (string.IsNullOrWhiteSpace(blockSubtype))
                    continue;

                _engineOverrides[blockSubtype] = new EngineOverrideDefinition(
                    blockSubtype,
                    Math.Max(1, entry.Priority),
                    entry.DisabledIntegrityRatio >= 0
                        ? (float?)ClampIntegrityRatio(entry.DisabledIntegrityRatio)
                        : null);
            }
        }

        public bool TryGetEngineOverride(string blockSubtype, out EngineOverrideDefinition definition)
        {
            definition = null;
            return !string.IsNullOrWhiteSpace(blockSubtype)
                   && _engineOverrides.TryGetValue(blockSubtype, out definition);
        }

        private static float ClampIntegrityRatio(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0;
            if (value < 0)
                return 0;
            if (value > 1)
                return 1;
            return value;
        }

        public sealed class EngineOverrideDefinition
        {
            public EngineOverrideDefinition(
                string blockSubtype,
                int priority,
                float? disabledIntegrityRatio)
            {
                BlockSubtype = blockSubtype;
                Priority = priority;
                DisabledIntegrityRatio = disabledIntegrityRatio;
            }

            public string BlockSubtype { get; }
            public int Priority { get; }
            public float? DisabledIntegrityRatio { get; }
        }
    }
}
