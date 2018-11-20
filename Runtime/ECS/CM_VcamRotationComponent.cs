using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamRotation : IComponentData
    {
        /// <summary>
        /// The world space focus point of the camera.  What the camera wants to look at.
        /// There is a special constant define to represent "nothing".  Be careful to 
        /// check for that (or check the HasLookAt property).
        /// </summary>
        public float3 lookAtPoint;

        /// <summary> Raw (un-corrected) world space orientation of this camera </summary>
        public quaternion raw;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamRotationComponent : ComponentDataWrapper<CM_VcamRotation> { } 
}
