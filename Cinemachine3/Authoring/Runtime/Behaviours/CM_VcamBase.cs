using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Cinemachine;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [RequireComponent(typeof(GameObjectEntity))]
    [SaveDuringPlay]
    public abstract class CM_VcamBase : MonoBehaviour
    {
        /// <summary>The priority will determine which camera becomes active based on the
        /// state of other cameras and this camera.  Higher numbers have greater priority.
        /// </summary>
        [NoSaveDuringPlay]
        [Tooltip("The priority will determine which camera becomes active based on the "
            + "state of other cameras and this camera.  Higher numbers have greater priority.")]
        public int priority = 10;

        /// <summary> Collection of parameters that influence how this virtual camera transitions from
        /// other virtual cameras </summary>
        public CinemachineVirtualCameraBase.TransitionParams transitions; // GML fixme

        /// <summary>Get the VirtualCamera representation of this vcam</summary>
        public VirtualCamera VirtualCamera { get { return VirtualCamera.FromEntity(Entity); } }

        /// <summary>Is the vcam currently conrolling a Camera?</summary>
        public bool IsLive { get { return VirtualCamera.IsLive; } }

        /// <summary>What channel this vcam is on</summary>
        public int ChannelValue { get { return VirtualCamera.ChannelValue; } }

        /// <summary>The CameraState object holds all of the information
        /// necessary to position the Unity camera.  It is the output of this class.</summary>
        public virtual CameraState State { get { return VirtualCamera.State; } }

        // GML todo: use conversion instead
        protected GameObjectEntity m_gameObjectEntity;
        public Entity Entity
        {
            get { return m_gameObjectEntity == null ? Entity.Null : m_gameObjectEntity.Entity; }
        }

        /// <summary>Get component data, with all the null checks.
        /// Returns default if nonexistant</summary>
        public T SafeGetComponentData<T>() where T : struct, IComponentData
        {
            var e = Entity;
            var m = World.Active?.EntityManager;
            if (m != null && m.Exists(e))
                if (m.HasComponent<T>(e))
                    return m.GetComponentData<T>(e);
            return new T();
        }

        /// <summary>Set component data, with all the null checks. Adds if necessary</summary>
        public void SafeSetComponentData<T>(T c) where T : struct, IComponentData
        {
            var e = Entity;
            var m = World.Active?.EntityManager;
            if (m != null && m.Exists(e))
            {
                if (m.HasComponent<T>(e))
                    m.SetComponentData(e, c);
                else
                    m.AddComponentData(e, c);
            }
        }

        protected virtual void PushValuesToEntityComponents()
        {
            var goh = new GameObjectEntityHelper(transform, true);
            if (goh.IsNull)
                return;

            // GML todo: change this.  Don't want to be tied to layers
            if (!goh.EntityManager.HasComponent<CM_VcamChannel>(goh.Entity)
                || ChannelValue != gameObject.layer)
            {
                goh.SafeSetSharedComponentData(new CM_VcamChannel
                {
                    channel = gameObject.layer
                });
            }

            goh.SafeSetComponentData(new CM_VcamPriority
            {
                priority = priority
                // GML todo: vcamSequence
            });

            goh.SafeAddComponentData(new CM_VcamShotQuality());
        }

        protected virtual void OnEnable()
        {
            m_gameObjectEntity = GetComponent<GameObjectEntity>();
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
