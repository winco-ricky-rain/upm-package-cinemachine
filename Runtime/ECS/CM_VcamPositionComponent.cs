using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamPosition : IComponentData
    {
        /// <summary> Raw (un-corrected) world space position of this camera </summary>
        public float3 raw;

        /// <summary>This is a way for the Body component to bypass aim damping,
        /// useful for when the body need to rotate its point of view, but does not
        /// want interference from the aim damping</summary>
        public float3 dampingBypass;

        /// <summary> Which way is up for this vcam (independent of its orientation).  World space unit vector. </summary>
        public float3 up;

        /// GML not sure where to put this
        public int previousFrameDataIsValid; // only one bit needed.  Can put more flags here
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamPositionComponent : ComponentDataWrapper<CM_VcamPosition> { } 
}
