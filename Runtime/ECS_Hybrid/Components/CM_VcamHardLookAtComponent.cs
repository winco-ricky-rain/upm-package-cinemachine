using Unity.Entities;
using Cinemachine.ECS;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(CinemachineCore.Stage.Aim)]
    public class CM_VcamHardLookAtComponent : ComponentDataWrapper<CM_VcamHardLookAt> { } 
}
