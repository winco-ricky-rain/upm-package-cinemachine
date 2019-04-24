using Cinemachine.ECS;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(CinemachineCore.Stage.Body)]
    [SaveDuringPlay]
    public class CM_VcamHardLockToTargetProxy : CM_VcamComponentProxyBase<CM_VcamHardLockToTarget> { }
}
