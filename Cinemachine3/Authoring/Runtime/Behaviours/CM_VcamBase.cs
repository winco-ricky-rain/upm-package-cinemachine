using UnityEngine;
using Unity.Entities;
using Cinemachine;
using Unity.Cinemachine.Common;
using Unity.Transforms;

namespace Unity.Cinemachine3.Authoring
{
    [SaveDuringPlay]
    public abstract class CM_VcamBase : CM_EntityProxyBase
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

        public override void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            base.Convert(entity, dstManager, conversionSystem);
            if (enabled)
            {
                // GML todo: change this.  Don't want to be tied to layers
                dstManager.AddSharedComponentData(entity, new CM_VcamChannel { channel = gameObject.layer });

                dstManager.AddComponentData(entity, new CM_VcamPriority { priority = priority });
                dstManager.AddComponentData(entity, new CM_VcamShotQuality());

                // GML temp stuff necessary for hybrid - how to get rid of it?
                if (!dstManager.HasComponent<Transform>(entity))
                    dstManager.AddComponentObject(entity, transform);
                if (!dstManager.HasComponent<CopyTransformFromGameObject>(entity))
                    dstManager.AddComponentData(entity, new CopyTransformFromGameObject());
            }
        }

        protected virtual void Update()
        {
        }
    }
}
