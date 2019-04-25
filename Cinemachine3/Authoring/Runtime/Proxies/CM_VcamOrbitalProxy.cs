using Unity.Mathematics;
using Cinemachine;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(PipelineStage.Body)]
    [SaveDuringPlay]
    public class CM_VcamOrbitalProxy : CM_VcamComponentProxyBase<CM_VcamOrbital>
    {
        private void OnValidate()
        {
            var v = Value;
            v.damping = math.max(float3.zero, v.damping);
            v.angularDamping = math.max(0, v.angularDamping);
            v.splineCurvature = math.clamp(v.splineCurvature, 0, 1);
            Value = v;
        }

        private void Reset()
        {
            Value = new CM_VcamOrbital
            {
                bindingMode = CM_VcamTransposerSystem.BindingMode.WorldSpace,
                damping = new float3(1, 1, 1),
                angularDamping = 1,
                top = new CM_VcamOrbital.Orbit { height = 10, radius = 4 },
                middle = new CM_VcamOrbital.Orbit { height = 2, radius = 8 },
                bottom = new CM_VcamOrbital.Orbit { height = 0, radius = 5 },
                splineCurvature = 0,
                horizontalAxis = new CM_InputAxis
                {
                    range = new float2(-180, 180),
                    wrap = true,
                    recentering = new CM_InputAxis.Recentering { wait = 1, time = 2 }
                },
                verticalAxis = new CM_InputAxis
                {
                    range = new float2(-100, 100),
                    recentering = new CM_InputAxis.Recentering { wait = 1, time = 2 }
                },
                radialAxis = new CM_InputAxis
                {
                    range = new float2(1, 10),
                    recentering = new CM_InputAxis.Recentering { wait = 1, time = 2 }
                },
            };
        }
    }
}
