using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    /// <summary>
    /// Describes the FOV and clip planes for a camera.  This generally mirrors the Unity Camera's 
    /// lens settings, and will be used to drive the Unity camera when the vcam is active.
    /// </summary>
    [Serializable]
    public struct CM_VcamLensState : IComponentData
    {
        /// <summary>
        /// This is the camera view in vertical degrees. For cinematic people, a 50mm lens
        /// on a super-35mm sensor would equal a 19.6 degree FOV.
        /// When using an orthographic camera, this defines the height, in world 
        /// co-ordinates, of the camera view.
        /// </summary>
        public float fov;

        /// <summary> The near clip plane for this LensSettings </summary>
        public float nearClip;

        /// <summary> The far clip plane for this LensSettings </summary>
        public float farClip;

        /// <summary> The dutch (tilt) to be applied to the camera. In degrees </summary>
        public float dutch;

        /// <summary> For physical cameras only: position of the gate relative to the film back </summary>
        public float2 lensShift;
    }

    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamLensStateComponent : ComponentDataWrapper<CM_VcamLensState> { } 
}
