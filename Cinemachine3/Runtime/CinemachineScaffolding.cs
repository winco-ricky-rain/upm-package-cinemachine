using Cinemachine.Utility;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    // Temporary stuff that will go away as Cinemachine3 becomes
    // fully independent of Cinemachine2
    public static class CinemachineScaffolding
    {
        /// <summary>Text description of a blend, for debugging</summary>
        public static string Description(this CM_BlendState blend)
        {
            var sb = CinemachineDebug.SBFromPool();
            var cam = CM_EntityVcam.GetEntityVcam(blend.cam);
            if (cam == null || !cam.IsValid)
                sb.Append("(none)");
            else
            {
                sb.Append("[");
                sb.Append(cam.Name);
                sb.Append("]");
            }
            sb.Append(" ");
            sb.Append((int)(blend.weight * 100f));
            sb.Append("% from ");
            var outgoingCam = CM_EntityVcam.GetEntityVcam(blend.outgoingCam);
            if (outgoingCam == null || !outgoingCam.IsValid)
                sb.Append("(none)");
            else
            {
                sb.Append("[");
                sb.Append(outgoingCam.Name);
                sb.Append("]");
            }
            string text = sb.ToString();
            CinemachineDebug.ReturnToPool(sb);
            return text;
        }

        public static CinemachineBlendDefinition GetBlendForVirtualCameras(
            this CinemachineBlenderSettings def,
            Entity fromCam, Entity toCam,
            CinemachineBlendDefinition defaultBlend)
        {
            var f = CM_EntityVcam.GetEntityVcam(fromCam);
            var t = CM_EntityVcam.GetEntityVcam(toCam);
            var fromName = f == null ? string.Empty : f.Name;
            var toName = t == null ? string.Empty : t.Name;
            return def.GetBlendForVirtualCameras(fromName, toName, defaultBlend);
        }

        /// <summary>Get the signal value at a given time, offset by a given amount</summary>
        public static float GetValueAt(
            this NoiseSettings.NoiseParams n, float time, float timeOffset)
        {
            float t = (n.Frequency * time) + timeOffset;
            return math.select(
                noise.cnoise(new float2(t, 0) - 0.5f) * n.Amplitude,
                math.cos(t * 2f * (float)math.PI) * n.Amplitude * 0.5f,
                n.Constant);
        }

        public static ECS.BlendCurve ToECS(this Cinemachine.BlendCurve c)
        {
            return new ECS.BlendCurve { A = c.A, B = c.B, bias = c.bias };
        }
    }
}
