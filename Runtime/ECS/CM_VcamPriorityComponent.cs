using System;
using Unity.Entities;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamPriority : IComponentData
    {
        /// <summary>GameObjet layer mask, brain will only see the vcams that pass 
        /// its layer filter</summary>
        public int vcamLayer;

        /// <summary>The priority will determine which camera becomes active based on the
        /// state of other cameras and this camera.  Higher numbers have greater priority.
        /// </summary>
        public int priority;

        /// <summary>Used as second key for priority sorting</summary>
        public int vcamSequence;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamPriorityComponent : ComponentDataWrapper<CM_VcamPriority> { } 
}
