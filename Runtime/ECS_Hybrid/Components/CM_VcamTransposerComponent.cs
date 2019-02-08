using Unity.Entities;
using Cinemachine.ECS;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(CinemachineCore.Stage.Body)]
    [SaveDuringPlay]
    public class CM_VcamTransposerComponent : CM_VcamComponentBase<CM_VcamTransposer> { }
}
