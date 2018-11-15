using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamTransposer : IComponentData
    {
        /// <summary>
        /// The coordinate space to use when interpreting the offset from the target
        /// </summary>
        public enum BindingMode
        {
            /// <summary>
            /// Camera will be bound to the Follow target using a frame of reference consisting
            /// of the target's local frame, with the tilt and roll zeroed out.
            /// </summary>
            LockToTargetWithWorldUp,
            /// <summary>
            /// Camera will be bound to the Follow target using a frame of reference consisting
            /// of the target's local frame, with the roll zeroed out.
            /// </summary>
            LockToTargetNoRoll,
            /// <summary>
            /// Camera will be bound to the Follow target using the target's local frame.
            /// </summary>
            LockToTarget,
            /// <summary>Camera will be bound to the Follow target using a world space offset.</summary>
            WorldSpace,
            /// <summary>Offsets will be calculated relative to the target, using Camera-local axes</summary>
            SimpleFollowWithWorldUp
        }
        /// <summary>The coordinate space to use when interpreting the offset from the target</summary>
        public BindingMode bindingMode;

        /// <summary>The distance which the transposer will attempt to maintain from the transposer subject</summary>
        public float3 followOffset;

        /// <summary>How aggressively the camera tries to maintain the offset in the 3 axes.
        /// Small numbers are more responsive, rapidly translating the camera to keep the target's
        /// offset.  Larger numbers give a more heavy slowly responding camera.
        /// Using different settings per axis can yield a wide range of camera behaviors</summary>
        public float3 damping;

        /// <summary>How aggressively the camera tries to track the target's rotation.  
        /// Small numbers are more responsive.  Larger numbers give a more heavy slowly responding camera.</summary>
        public float angularDamping;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamTransposerComponent : ComponentDataWrapper<CM_VcamTransposer> { } 

    [Serializable]
    public struct CM_VcamTransposerState : IComponentData
    {
        /// State information used for damping
        public float3 previousTargetPosition;
        public quaternion previousTargetRotation;
    }
}
