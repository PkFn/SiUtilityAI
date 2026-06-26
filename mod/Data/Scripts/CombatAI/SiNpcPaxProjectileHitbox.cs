using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Pax.Misc;
using Sandbox.ModAPI;
using VRage.Components;
using VRage.Components.Interfaces;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Definitions;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.ObjectBuilders;
using VRageMath;

namespace Si.UtilityAI
{
    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcPaxProjectileHitboxComponent : MyObjectBuilder_EntityComponent
    {
    }

    [MyObjectBuilderDefinition]
    [XmlSerializerAssembly("MedievalEngineers.ObjectBuilders.XmlSerializers")]
    public class MyObjectBuilder_SiNpcPaxProjectileHitboxComponentDefinition : MyObjectBuilder_EntityComponentDefinition
    {
        public float Width;
        public float Depth;
        public float Height;
        public float CenterHeight;
        public float DamageMultiplier;
    }

    [MyDefinitionType(typeof(MyObjectBuilder_SiNpcPaxProjectileHitboxComponentDefinition))]
    public class SiNpcPaxProjectileHitboxComponentDefinition : MyEntityComponentDefinition
    {
        public float Width { get; private set; }
        public float Depth { get; private set; }
        public float Height { get; private set; }
        public float CenterHeight { get; private set; }
        public float DamageMultiplier { get; private set; }

        public bool IsUsable => Width > 0 && Depth > 0 && Height > 0 && DamageMultiplier > 0;

        protected override void Init(MyObjectBuilder_DefinitionBase builder)
        {
            base.Init(builder);
            var ob = (MyObjectBuilder_SiNpcPaxProjectileHitboxComponentDefinition)builder;
            Width = Math.Max(0, ob.Width);
            Depth = Math.Max(0, ob.Depth);
            Height = Math.Max(0, ob.Height);
            CenterHeight = ob.CenterHeight;
            DamageMultiplier = Math.Max(0, ob.DamageMultiplier);
        }
    }

    [MyComponent(typeof(MyObjectBuilder_SiNpcPaxProjectileHitboxComponent))]
    [MyDefinitionRequired(typeof(SiNpcPaxProjectileHitboxComponentDefinition))]
    public class SiNpcPaxProjectileHitboxComponent : MyEntityComponent
    {
        private const int FaceCount = 6;
        private const int RecentProjectileCapacity = 64;

        private readonly Queue<long> _recentProjectileIds = new Queue<long>();
        private readonly HashSet<long> _recentProjectileSet = new HashSet<long>();

        private SiNpcPaxProjectileHitboxComponentDefinition _definition;
        private MyEntity[] _faceEntities;
        private Vector3[][] _faceVertices;

        public override bool IsSerialized => false;

        public override void Init(MyEntityComponentDefinition definition)
        {
            base.Init(definition);
            _definition = (SiNpcPaxProjectileHitboxComponentDefinition)definition;
        }

        public override void OnAddedToScene()
        {
            base.OnAddedToScene();
            RegisterColliders();
        }

        public override void OnRemovedFromScene()
        {
            UnregisterColliders();
            base.OnRemovedFromScene();
        }

        public override void OnBeforeRemovedFromContainer()
        {
            UnregisterColliders();
            base.OnBeforeRemovedFromContainer();
        }

        private void RegisterColliders()
        {
            if (_faceEntities != null || !IsAuthoritative || _definition == null || !_definition.IsUsable)
                return;

            _faceVertices = CreateBoxFaces(_definition);
            _faceEntities = new MyEntity[FaceCount];
            for (var i = 0; i < FaceCount; i++)
            {
                var face = new MyEntity { Save = false };
                face.WorldMatrix = Entity.WorldMatrix;
                _faceEntities[i] = face;
                VirtualColliders.AddCollider(Entity, face, _faceVertices[i], OnVirtualProjectileHit);
            }

            AddFixedUpdate(SyncColliderTransforms);
        }

        private void UnregisterColliders()
        {
            if (_faceEntities == null)
                return;

            RemoveFixedUpdate(SyncColliderTransforms);
            for (var i = 0; i < _faceEntities.Length; i++)
            {
                var face = _faceEntities[i];
                if (face != null)
                    VirtualColliders.RemoveCollider(Entity, face);
            }

            _faceEntities = null;
            _faceVertices = null;
            _recentProjectileIds.Clear();
            _recentProjectileSet.Clear();
        }

        [FixedUpdate(false)]
        private void SyncColliderTransforms()
        {
            if (_faceEntities == null || Entity == null)
                return;

            var world = Entity.WorldMatrix;
            for (var i = 0; i < _faceEntities.Length; i++)
                if (_faceEntities[i] != null)
                    _faceEntities[i].WorldMatrix = world;
        }

        private void OnVirtualProjectileHit(int damage, long projectileId)
        {
            if (damage <= 0 || IsDuplicateProjectile(projectileId))
                return;

            var receiver = Entity?.Components.Get<SiNpcDamageComponent>();
            if (receiver == null || receiver.IsDead)
                return;

            var amount = damage * _definition.DamageMultiplier;
            if (amount <= 0)
                return;

            var hitInfo = new MyHitInfo { Position = Entity.WorldMatrix.Translation };
            var damageInfo = new MyDamageInformation(amount, MyDamageType.Bullet, null, hitInfo)
            {
                DamagedEntity = Entity
            };
            receiver.DoDamage(damageInfo);
        }

        private bool IsDuplicateProjectile(long projectileId)
        {
            if (projectileId == 0)
                return false;
            if (_recentProjectileSet.Contains(projectileId))
                return true;

            _recentProjectileIds.Enqueue(projectileId);
            _recentProjectileSet.Add(projectileId);
            while (_recentProjectileIds.Count > RecentProjectileCapacity)
                _recentProjectileSet.Remove(_recentProjectileIds.Dequeue());
            return false;
        }

        private static Vector3[][] CreateBoxFaces(SiNpcPaxProjectileHitboxComponentDefinition definition)
        {
            var halfWidth = definition.Width * 0.5f;
            var halfDepth = definition.Depth * 0.5f;
            var minY = definition.CenterHeight - definition.Height * 0.5f;
            var maxY = definition.CenterHeight + definition.Height * 0.5f;

            return new[]
            {
                Quad(
                    new Vector3(-halfWidth, minY, halfDepth),
                    new Vector3(halfWidth, minY, halfDepth),
                    new Vector3(halfWidth, maxY, halfDepth),
                    new Vector3(-halfWidth, maxY, halfDepth)),
                Quad(
                    new Vector3(halfWidth, minY, -halfDepth),
                    new Vector3(-halfWidth, minY, -halfDepth),
                    new Vector3(-halfWidth, maxY, -halfDepth),
                    new Vector3(halfWidth, maxY, -halfDepth)),
                Quad(
                    new Vector3(halfWidth, minY, halfDepth),
                    new Vector3(halfWidth, minY, -halfDepth),
                    new Vector3(halfWidth, maxY, -halfDepth),
                    new Vector3(halfWidth, maxY, halfDepth)),
                Quad(
                    new Vector3(-halfWidth, minY, -halfDepth),
                    new Vector3(-halfWidth, minY, halfDepth),
                    new Vector3(-halfWidth, maxY, halfDepth),
                    new Vector3(-halfWidth, maxY, -halfDepth)),
                Quad(
                    new Vector3(-halfWidth, maxY, halfDepth),
                    new Vector3(halfWidth, maxY, halfDepth),
                    new Vector3(halfWidth, maxY, -halfDepth),
                    new Vector3(-halfWidth, maxY, -halfDepth)),
                Quad(
                    new Vector3(-halfWidth, minY, -halfDepth),
                    new Vector3(halfWidth, minY, -halfDepth),
                    new Vector3(halfWidth, minY, halfDepth),
                    new Vector3(-halfWidth, minY, halfDepth)),
            };
        }

        private static Vector3[] Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) =>
            new[] { a, b, c, d };

        private static bool IsAuthoritative =>
            MyMultiplayerModApi.Static == null || MyMultiplayerModApi.Static.IsServer;
    }
}
