using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    /// <summary>
    /// Subjective estimation of how "good" the shot is.
    /// Larger values mean better quality.  Default is 1.
    /// </summary>
    [Serializable]
    public struct CM_VcamShotQuality : IComponentData
    {
        public float value;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamShotQualityComponent : ComponentDataWrapper<CM_VcamShotQuality> { } 
}
