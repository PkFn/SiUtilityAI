using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Inventory;
using VRageMath;

namespace Si.UtilityAI
{
    internal sealed class SiNearbyEntityScanner
    {
        internal struct EntityCandidate
        {
            public MyEntity Entity;
            public double DistanceSquared;
        }

        internal struct InventoryCandidate
        {
            public MyEntity Entity;
            public MyInventoryBase Inventory;
            public double DistanceSquared;
        }

        public void ScanEntities(
            in Vector3D position,
            double radius,
            List<EntityCandidate> results,
            Func<MyEntity, bool> filter = null)
        {
            results?.Clear();
            if (results == null || radius <= 0 || MyAPIGateway.Entities == null)
                return;

            var sphere = new BoundingSphereD(position, radius);
            var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
            if (entities == null)
                return;

            var radiusSquared = radius * radius;
            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity == null
                    || entity.Closed
                    || entity.MarkedForClose
                    || (filter != null && !filter(entity)))
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(position, entity.WorldMatrix.Translation);
                if (distanceSquared > radiusSquared)
                    continue;

                results.Add(new EntityCandidate
                {
                    Entity = entity,
                    DistanceSquared = distanceSquared,
                });
            }

            results.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
        }

        public void ScanInventories(
            in Vector3D position,
            double radius,
            List<InventoryCandidate> results,
            Func<MyEntity, bool> filter = null)
        {
            results?.Clear();
            if (results == null || radius <= 0 || MyAPIGateway.Entities == null)
                return;

            var sphere = new BoundingSphereD(position, radius);
            var entities = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
            if (entities == null)
                return;

            var radiusSquared = radius * radius;
            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity == null
                    || entity.Closed
                    || entity.MarkedForClose
                    || (filter != null && !filter(entity)))
                    continue;

                var inventory = ResolveInventory(entity);
                if (inventory == null || inventory.ItemCount <= 0)
                    continue;

                var distanceSquared = Vector3D.DistanceSquared(position, entity.WorldMatrix.Translation);
                if (distanceSquared > radiusSquared)
                    continue;

                results.Add(new InventoryCandidate
                {
                    Entity = entity,
                    Inventory = inventory,
                    DistanceSquared = distanceSquared,
                });
            }

            results.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
        }

        private static MyInventoryBase ResolveInventory(MyEntity entity)
        {
            if (entity?.Components == null)
                return null;

            if (entity.Components.TryGet<MyInventoryBase>(out var inventory) && inventory != null)
                return inventory;

            string ignored;
            return SiNpcEquipmentHelper.FindInventory(entity, out ignored);
        }
    }
}
