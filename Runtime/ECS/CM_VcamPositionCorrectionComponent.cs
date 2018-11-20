using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamPositionCorrection : IComponentData
    {
        /// <summary>
        /// Position correction.  This will be added to the raw position.
        /// This value doesn't get fed back into the system when calculating the next frame.
        /// Can be noise, or smoothing, or both, or something else.
        /// </summary>
        public float3 value;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamPositionCorrectionComponent : ComponentDataWrapper<CM_VcamPositionCorrection> { } 
}
