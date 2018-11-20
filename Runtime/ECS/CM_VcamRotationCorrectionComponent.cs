using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamRotationCorrection : IComponentData
    {
        /// <summary>
        /// Rotation correction.  This will be added to the raw orientation.
        /// This value doesn't get fed back into the system when calculating the next frame.
        /// Can be noise, or smoothing, or both, or something else.
        /// </summary>
        public quaternion value;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamRotationCorrectionComponent : ComponentDataWrapper<CM_VcamRotationCorrection> { } 
}
