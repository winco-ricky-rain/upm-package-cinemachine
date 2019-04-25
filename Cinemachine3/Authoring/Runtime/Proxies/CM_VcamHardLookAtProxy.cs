using Cinemachine;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(CinemachineCore.Stage.Aim)]
    [SaveDuringPlay]
    public class CM_VcamHardLookAtProxy : CM_VcamComponentProxyBase<CM_VcamHardLookAt> { }
}
