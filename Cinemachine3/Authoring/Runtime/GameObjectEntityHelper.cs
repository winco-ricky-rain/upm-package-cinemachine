using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Cinemachine3
{
    /// <summary>
    /// Helper functions for doing stuff with Transforms that are also Enities.
    /// Instantiate as needed, don't keep.
    /// </summary>
    public struct GameObjectEntityHelper
    {
        /// <summary>Constructor to wrap a transform</summary>
        /// <param name="t">The transform that should also be an Entity</param>
        /// <param name="createEntity">Force creation of entity if true</param>
        /// GML todo something better here
        public GameObjectEntityHelper(Transform t, bool createEntity)
        {
            Transform = t;
            EntityManager = World.Active?.EntityManager;
            Entity = Entity.Null;
            if (t != null && EntityManager != null)
            {
                var goe = Transform.GetComponent<GameObjectEntity>();
                if (goe == null && createEntity)
                    goe = Transform.gameObject.AddComponent<GameObjectEntity>();
                if (goe != null)
                    Entity = goe.Entity;
            }
        }

        /// <summary>The entity that holds the CM_Channel component</summary>
        public Transform Transform { get; private set; }
        public EntityManager EntityManager { get; private set; }
        public Entity Entity { get; private set; }

        /// <summary>Is this a null entity?</summary>
        public bool IsNull { get { return Entity == Entity.Null; } }

        /// <summary>A "blank" Entity object that does not refer to an actual entity.</summary>
        public static GameObjectEntityHelper Null => new GameObjectEntityHelper();

        /// <summary>Add component data, but only if absent</summary>
        public void SafeAddComponentData<T>(T c) where T : struct, IComponentData
        {
            if (!IsNull && !EntityManager.HasComponent<T>(Entity))
                EntityManager.AddComponentData(Entity, c);
        }

        /// <summary>Get component data, with all the null checks.
        /// Returns default if nonexistant</summary>
        public T SafeGetComponentData<T>() where T : struct, IComponentData
        {
            if (!IsNull && EntityManager.HasComponent<T>(Entity))
                return EntityManager.GetComponentData<T>(Entity);
            return new T();
        }

        /// <summary>Set component data, with all the null checks. Will add if necessary</summary>
        public void SafeSetComponentData<T>(T c) where T : struct, IComponentData
        {
            if (!IsNull)
            {
                if (EntityManager.HasComponent<T>(Entity))
                    EntityManager.SetComponentData(Entity, c);
                else
                    EntityManager.AddComponentData(Entity, c);
            }
        }

        /// <summary>Set component data, with all the null checks. Will add if necessary</summary>
        public void SafeSetSharedComponentData<T>(T c) where T : struct, ISharedComponentData
        {
            if (!IsNull)
            {
                if (EntityManager.HasComponent<T>(Entity))
                    EntityManager.SetSharedComponentData(Entity, c);
                else
                    EntityManager.AddSharedComponentData(Entity, c);
            }
        }

        /// GML todo something better here
        public void EnsureTransformCompliance()
        {
            SafeAddComponentData(new LocalToWorld { Value = float4x4.identity });
            SafeAddComponentData(new CopyTransformFromGameObject());
        }
    }
}
