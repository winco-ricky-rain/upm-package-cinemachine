using Unity.Entities;

namespace Cinemachine.ECS_Hybrid
{
    public abstract class CM_ComponentProxyBase<T> : ComponentDataProxy<T> where T : struct, IComponentData
    {
        public bool TryGetEntityAndManager(out EntityManager entityManager, out Entity entity)
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

        public Entity Entity
        {
            get
            {
                if (TryGetEntityAndManager(out EntityManager m, out Entity entity))
                    return entity;
                return Entity.Null;
            }
        }

        protected EntityManager ActiveEntityManager
        {
            get
            {
                if (TryGetEntityAndManager(out EntityManager m, out Entity entity))
                    return m;
                return null;
            }
        }

        public CT GetEntityComponentData<CT>() where CT : struct, IComponentData
        {
            if (TryGetEntityAndManager(out EntityManager m, out Entity entity))
                if (m.HasComponent<CT>(entity))
                    return m.GetComponentData<CT>(entity);
            return new CT();
        }

        public CM_VcamBase Vcam { get { return GetComponent<CM_VcamBase>(); } }
    }
}
