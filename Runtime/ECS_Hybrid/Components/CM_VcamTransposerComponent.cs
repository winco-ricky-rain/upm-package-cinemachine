using Unity.Entities;
using Cinemachine.ECS;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(CinemachineCore.Stage.Body)]
    public class CM_VcamTransposerComponent : ComponentDataWrapper<CM_VcamTransposer> { } 
}
