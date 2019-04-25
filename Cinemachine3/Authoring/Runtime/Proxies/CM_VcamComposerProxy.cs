using Unity.Mathematics;
using Cinemachine;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(PipelineStage.Aim)]
    [SaveDuringPlay]
    public class CM_VcamComposerProxy : CM_VcamComponentProxyBase<CM_VcamComposer>
    {
        private void OnValidate()
        {
            var v = Value;
            v.damping = math.max(float2.zero, v.damping);
            v.screenPosition = math.clamp(v.screenPosition, new float2(-1, -1), new float2(1, 1));
            v.deadZoneSize = math.max(float2.zero, v.deadZoneSize);
            v.SetSoftGuideRect(v.GetSoftGuideRect());
            v.SetHardGuideRect(v.GetHardGuideRect());
            v.softZoneBias = math.clamp(v.softZoneBias, new float2(-1, -1), new float2(1, 1));
            Value = v;
        }

        private void Reset()
        {
            Value = new CM_VcamComposer
            {
                damping = new float2(1, 1),
                softZoneSize = new float2(0.6f, 0.6f),
                centerOnActivate = true
            };
        }
    }
}
