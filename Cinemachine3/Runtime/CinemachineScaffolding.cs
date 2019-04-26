using Unity.Entities;
using Unity.Mathematics;
using Unity.Cinemachine3;
using Cinemachine.Utility;
using System.Runtime.CompilerServices;
using Unity.Cinemachine.Common;

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
            var cam = VirtualCamera.FromEntity(blend.cam);
            sb.Append(cam.Name);
            sb.Append(" ");
            sb.Append((int)(blend.weight * 100f));
            sb.Append("% from ");
            var outgoingCam = VirtualCamera.FromEntity(blend.outgoingCam);
            sb.Append(outgoingCam.Name);
            string text = sb.ToString();
            CinemachineDebug.ReturnToPool(sb);
            return text;
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
    }
}
