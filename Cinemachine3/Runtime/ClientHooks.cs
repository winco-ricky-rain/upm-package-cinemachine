using Unity.Cinemachine.Common;
using Unity.Entities;

namespace Unity.Cinemachine3
{
    public static class ClientHooks
    {
        /// <summary>Delegate for overriding Unity's default input system.  Returns the value
        /// of the named axis.</summary>
        public delegate float AxisInputDelegate(string axisName);

        /// <summary>Delegate for overriding Unity's default input system.
        /// If you set this, then your delegate will be called instead of
        /// System.Input.GetAxis(axisName) whenever in-game user input is needed.</summary>
        public static AxisInputDelegate GetInputAxis = UnityEngine.Input.GetAxis;

        /// <summary>Hook for custom blend - called whenever a blend is created,
        /// allowing client to override the blend definition</summary>
        public delegate CinemachineBlendDefinition CreateBlendDelegate(
            Entity context, VirtualCamera fromCam, VirtualCamera toCam,
            CinemachineBlendDefinition defaultBlend);

        /// <summary>Hook for custom blend - called whenever a blend is created,
        /// allowing client to override the blend definition</summary>
        public static CreateBlendDelegate OnCreateBlend;
    }
}
