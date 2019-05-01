using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(PipelineStage.Body)]
    [SaveDuringPlay]
    public class CM_VcamHardLockToTargetProxy : CM_VcamComponentBase<CM_VcamHardLockToTarget> { }
}
