using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamHardLookAt : IComponentData
    {
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamHardLookAtComponent : ComponentDataWrapper<CM_VcamHardLookAt> { } 
}
