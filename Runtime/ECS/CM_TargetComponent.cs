using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_Target : IComponentData
    {
        public float radius;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_TargetComponent : ComponentDataWrapper<CM_Target> { } 
}
