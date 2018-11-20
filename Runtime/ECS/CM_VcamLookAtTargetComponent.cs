using System;
using Unity.Entities;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamLookAtTarget : IComponentData
    {
        public Entity target;
    }
    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamLookAtTargetComponent : ComponentDataWrapper<CM_VcamLookAtTarget> { } 
}

