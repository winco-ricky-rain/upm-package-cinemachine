using UnityEngine;
using Cinemachine;
using Unity.Cinemachine.Common;
using Unity.Entities;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("Cinemachine/CM_Vcam")]
    public class CM_Vcam : CM_VcamBase
    {
        /// <summary>Object for the camera children wants to move with (the body target)</summary>
        [Tooltip("Object for the camera children wants to move with (the body target).")]
        [NoSaveDuringPlay]
        public Transform followTarget = null;

        /// <summary>Object for the camera children to look at (the aim target)</summary>
        [Tooltip("Object for the camera children to look at (the aim target).")]
        [NoSaveDuringPlay]
        public Transform lookAtTarget = null;

        /// <summary>Specifies the LensSettings of this Virtual Camera.
        /// These settings will be transferred to the Unity camera when the vcam is live.</summary>
        [Tooltip("Specifies the lens properties of this Virtual Camera.  This generally mirrors "
            + "the Unity Camera's lens settings, and will be used to drive the Unity camera when "
            + "the vcam is active.")]
        [LensSettingsProperty]
        public LensSettings lens = LensSettings.Default;

        /// <summary>API for the editor, to make the dragging of position handles behave better.</summary>
        public bool UserIsDragging { get; set; }

        public override void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            base.Convert(entity, dstManager, conversionSystem);
            if (enabled)
            {
                dstManager.AddComponentData(entity, new CM_VcamLens
                {
                    fov = lens.Orthographic ? lens.OrthographicSize : lens.FieldOfView,
                    nearClip = lens.NearClipPlane,
                    farClip = lens.FarClipPlane,
                    dutch = lens.Dutch,
                    lensShift = lens.LensShift
                });

                dstManager.AddComponentData(entity, new CM_VcamFollowTarget());
                dstManager.AddComponentData(entity, new CM_VcamLookAtTarget());
            }
        }

        protected override void Update()
        {
            base.Update();

            // Make sure the target entities are properly set up
            SafeSetComponentData(new CM_VcamFollowTarget
                { target = CM_TargetProxy.ValidateTarget(followTarget, true) });
            SafeSetComponentData(new CM_VcamLookAtTarget
                { target = CM_TargetProxy.ValidateTarget(lookAtTarget, true) });
        }
    }
}
