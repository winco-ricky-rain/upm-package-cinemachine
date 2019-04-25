using Unity.Entities;
using Unity.Mathematics;
using Unity.Cinemachine3;
using Cinemachine.Utility;
using System.Runtime.CompilerServices;

namespace Cinemachine
{
    // Temporary stuff that will go away as Cinemachine3 becomes
    // fully independent of old Cinemachine
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetValueAtECS(
            this NoiseSettings.NoiseParams n, float time, float timeOffset)
        {
            float t = (n.Frequency * time) + timeOffset;
            return math.select(
                noise.cnoise(new float2(t, 0) - 0.5f) * n.Amplitude,
                math.cos(t * 2f * (float)math.PI) * n.Amplitude * 0.5f,
                n.Constant);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Unity.Cinemachine3.BlendCurve ToECS(this Cinemachine.BlendCurve c)
        {
            return new Unity.Cinemachine3.BlendCurve { A = c.A, B = c.B, bias = c.bias };
        }
    }
}
