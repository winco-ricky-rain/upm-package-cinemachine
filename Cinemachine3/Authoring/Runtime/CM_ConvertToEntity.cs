using Unity.Entities;
using UnityEngine;

namespace Unity.Cinemachine3.Authoring
{
    public class CM_ConvertToEntity : MonoBehaviour
    {
        Entity _entity;
        public Entity Entity
        {
            get
            {
                if (_entity == Entity.Null)
                    _entity = Convert();
                return _entity;
            }
        }

        private void Awake()
        {
            if (transform.parent != null && transform.parent.GetComponentInParent<CM_ConvertToEntity>() != null)
                Debug.LogWarning(
                    $"Temporary implementation: {name} cannot be nested inside a CM_ConvertToEntity hierarchy. Please remove it", this);
            if (GetComponentInParent<ConvertToEntity>() != null)
                Debug.LogWarning(
                    $"Temporary implementation: {name} cannot be part of a ConvertToEntity hierarchy. Please remove it", this);
            if (GetComponent<GameObjectEntity>() != null)
                Debug.LogWarning(
                    $"Temporary implementation: {name} cannot have a GameObjectEntity component. Please remove it", this);
        }

        Entity Convert()
        {
            DefaultWorldInitialization.DefaultLazyEditModeInitialize();
            var w = World.Active;
            if (w != null)
                return GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, w);
            Debug.LogWarning(
                "CM_ConvertToEntity failed because there was no Active World", this);
            return Entity.Null;
        }

        public void DestroyEntity()
        {
            var w = World.Active;
            if (w != null && _entity != Entity.Null)
                w.EntityManager.DestroyEntity(_entity);
            _entity = Entity.Null;
        }
    }
}
