using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [SaveDuringPlay]
    [AddComponentMenu("")] // Don't display in add component menu
    public class CM_PathWaypointsProxy : DynamicBufferProxy<CM_PathWaypointElement>
    {
        bool TryGetEntityAndManager(out EntityManager entityManager, out Entity entity)
        {
            entityManager = null;
            entity = Entity.Null;
            // gameObject is not initialized yet in native when OnBeforeSerialized() is called via SmartReset()
            if (gameObject == null)
                return false;
            var gameObjectEntity = GetComponent<GameObjectEntity>();
            if (gameObjectEntity == null)
                return false;
            if (gameObjectEntity.EntityManager == null)
                return false;
            if (!gameObjectEntity.EntityManager.Exists(gameObjectEntity.Entity))
                return false;
            entityManager = gameObjectEntity.EntityManager;
            entity = gameObjectEntity.Entity;
            return true;
        }

        Entity Entity
        {
            get
            {
                if (TryGetEntityAndManager(out EntityManager m, out Entity entity))
                    return entity;
                return Entity.Null;
            }
        }

        protected override void ValidateSerializedData(List<CM_PathWaypointElement> serializedData)
        {
            var pathSystem = World.Active?.GetExistingSystem<CM_PathSystem>();
            pathSystem?.InvalidatePathCache(Entity);
        }
    }
}
