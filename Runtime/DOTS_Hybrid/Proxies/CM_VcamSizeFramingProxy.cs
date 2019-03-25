using Cinemachine.ECS;
using Unity.Mathematics;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_VcamSizeFramingProxy : CM_ComponentProxyBase<CM_VcamSizeFraming>
    {
        private void OnValidate()
        {
            var v = Value;
            v.screenFit = math.max(CM_VcamSizeFraming.kMinScreenFitSize, v.screenFit);
            v.screenFit.y = math.max(v.screenFit.x, v.screenFit.y);
            v.damping = math.max(0, v.damping);
            v.dollyRange.y = math.max(v.dollyRange.x, v.dollyRange.y);
            v.targetDistance = math.max(float2.zero, v.targetDistance);
            v.targetDistance.y = math.max(v.targetDistance.x, v.targetDistance.y);
            v.fovRange.x = math.clamp(v.fovRange.x, 1, 179);
            v.fovRange.y = math.clamp(v.fovRange.y, v.fovRange.x, 179);
            v.orthoSizeRange.x = math.max(0, v.orthoSizeRange.x);
            v.orthoSizeRange.y = math.max(v.orthoSizeRange.x, v.orthoSizeRange.y);
            Value = v;
        }

        private void Reset()
        {
            Value = new CM_VcamSizeFraming
            {
                screenFit = 0.8f,
                damping = 1f,
                framingMode = CM_VcamSizeFraming.FramingMode.Horizontal,
                adjustmentMode = CM_VcamSizeFraming.AdjustmentMode.DollyThenZoom,
                dollyRange = new float2(-5000f, 5000f),
                targetDistance = new float2(1, 5000f),
                fovRange = new float2(3, 60),
                orthoSizeRange = new float2(1, 5000)
            };
        }
    }
}
