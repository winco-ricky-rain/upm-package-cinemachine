using System;
using Unity.Entities;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamFollowTarget : IComponentData
    {
        public Entity target;
    }
    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamFollowTargetComponent : ComponentDataWrapper<CM_VcamFollowTarget> { } 
}
