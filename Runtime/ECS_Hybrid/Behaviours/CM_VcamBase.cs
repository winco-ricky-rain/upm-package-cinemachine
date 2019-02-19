using UnityEngine;
using Unity.Entities;
using Cinemachine.ECS;
using Unity.Transforms;

namespace Cinemachine.ECS_Hybrid
{
    [RequireComponent(typeof(GameObjectEntity))]
    public abstract class CM_VcamBase : MonoBehaviour, ICinemachineCamera
    {
        /// <summary>The priority will determine which camera becomes active based on the
        /// state of other cameras and this camera.  Higher numbers have greater priority.
        /// </summary>
        [NoSaveDuringPlay]
        [Tooltip("The priority will determine which camera becomes active based on the "
            + "state of other cameras and this camera.  Higher numbers have greater priority.")]
        public int m_Priority = 10;

        /// <summary> Collection of parameters that influence how this virtual camera transitions from
        /// other virtual cameras </summary>
        public CinemachineVirtualCameraBase.TransitionParams m_Transitions; // GML fixme

        public ICinemachineCamera ParentCamera { get { return null; } }
        public bool IsLiveChild(ICinemachineCamera vcam) { return false; }
        public void UpdateCameraState(Vector3 worldUp, float deltaTime) {}

        public bool IsLive
        {
            get
            {
                var m = World.Active?.GetExistingManager<CM_ChannelSystem>();
                return m == null ? false : m.IsLive(this);
            }
        }

        public Entity AsEntity { get { return Entity; }}

        public virtual bool IsValid { get { return !(this == null); } }

        /// <summary>Get the name of the Virtual Camera</summary>
        public virtual string Name { get { return name; } }

        /// <summary>The CameraState object holds all of the information
        /// necessary to position the Unity camera.  It is the output of this class.</summary>
        public virtual CameraState State
        {
            get { return (CM_EntityVcam.StateFromEntity(Entity)); }
        }

        /// <summary>Gets a brief debug description of this virtual camera, for
        /// use when displayiong debug info</summary>
        public virtual string Description { get { return string.Empty; }}

        public void OnTargetObjectWarped(Transform target, Vector3 positionDelta) {}
        public void OnTransitionFromCamera(ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime) {}
        public void InternalUpdateCameraState(Vector3 worldUp, float deltaTime) {}

        protected GameObjectEntity m_gameObjectEntityComponent;

        protected Entity Entity
        {
            get
            {
                return m_gameObjectEntityComponent == null
                    ? Entity.Null : m_gameObjectEntityComponent.Entity;
            }
        }

        protected EntityManager ActiveEntityManager
        {
            get { return World.Active?.GetExistingManager<EntityManager>(); }
        }

        public T GetEntityComponentData<T>() where T : struct, IComponentData
        {
            var m = ActiveEntityManager;
            if (m != null)
                if (m.HasComponent<T>(Entity))
                    return m.GetComponentData<T>(Entity);
            return new T();
        }

        protected CM_ChannelState ParentChannelState
        {
            get
            {
                var m = World.Active?.GetExistingManager<CM_ChannelSystem>();
                if (m != null)
                    return m.GetChannelState(ParentChannel);
                return new CM_ChannelState();
            }
        }

        public int ParentChannel { get { return gameObject.layer; } } // GML is this the right thing?


#if true // GML todo something here
        protected Entity EnsureTargetCompliance(Transform target)
        {
            if (target == null)
                return Entity.Null;

            var m = ActiveEntityManager;
            if (m == null)
                return Entity.Null;

            var goe = target.GetComponent<GameObjectEntity>();
            if (goe == null)
                goe = target.gameObject.AddComponent<GameObjectEntity>();

            var e = goe.Entity;
            if (!m.HasComponent<CM_Target>(e))
                m.AddComponentData(e, new CM_Target());

            if (!m.HasComponent<Position>(e))
                m.AddComponentData(e, new Position());
            if (!m.HasComponent<Rotation>(e))
                m.AddComponentData(e, new Rotation());
            if (!m.HasComponent<CopyTransformFromGameObject>(e))
                m.AddComponentData(e, new CopyTransformFromGameObject());

            return e;
        }
#endif

        protected virtual void PushValuesToEntityComponents()
        {
            var m = ActiveEntityManager;
            if (m == null || !m.Exists(Entity))
                return;

            if (!m.HasComponent<CM_VcamChannel>(Entity))
                m.AddComponentData(Entity, new CM_VcamChannel()); // GML todo: vcamSequence
            if (!m.HasComponent<CM_VcamPriority>(Entity))
                m.AddComponentData(Entity, new CM_VcamPriority()); // GML todo: vcamSequence
            if (!m.HasComponent<CM_VcamShotQuality>(Entity))
                m.AddComponentData(Entity, new CM_VcamShotQuality());

            m.SetComponentData(Entity, new CM_VcamChannel
            {
                channel = ParentChannel
            });
            m.SetComponentData(Entity, new CM_VcamPriority
            {
                priority = m_Priority
                // GML todo: vcamSequence
            });
        }

        protected virtual void OnEnable()
        {
            m_gameObjectEntityComponent = GetComponent<GameObjectEntity>();
            PushValuesToEntityComponents();
        }

        protected virtual void OnDisable()
        {
        }

        // GML: Needed in editor only, probably, only if something is dirtied
        protected virtual void Update()
        {
            PushValuesToEntityComponents();
        }
    }
}
