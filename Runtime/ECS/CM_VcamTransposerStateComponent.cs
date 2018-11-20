using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamTransposerState : IComponentData
    {
        /// State information used for damping
        public float3 previousTargetPosition;
        public quaternion previousTargetRotation;
    }
    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamTransposerStateComponent : ComponentDataWrapper<CM_VcamTransposerState> { } 
}
