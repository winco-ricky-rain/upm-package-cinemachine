using Cinemachine;

namespace Unity.Cinemachine3.Authoring
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(CinemachineCore.Stage.Body)]
    [SaveDuringPlay]
    public class CM_VcamHardLockToTargetProxy : CM_VcamComponentProxyBase<CM_VcamHardLockToTarget> { }
}
