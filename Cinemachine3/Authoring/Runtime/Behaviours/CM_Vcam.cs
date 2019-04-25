using UnityEngine;
using Unity.Transforms;
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

        protected override void PushValuesToEntityComponents()
        {
            base.PushValuesToEntityComponents();

            var m = World.Active?.EntityManager;
            if (m == null || !m.Exists(Entity))
                return;

            if (!m.HasComponent<LocalToWorld>(Entity))
                m.AddComponentData(Entity, new LocalToWorld());
            if (!m.HasComponent<CopyTransformFromGameObject>(Entity))
                m.AddComponentData(Entity, new CopyTransformFromGameObject());

            if (!m.HasComponent<CM_VcamLens>(Entity))
                m.AddComponentData(Entity, CM_VcamLens.Default);
            m.SetComponentData(Entity, new CM_VcamLens
            {
                fov = lens.Orthographic ? lens.OrthographicSize : lens.FieldOfView,
                nearClip = lens.NearClipPlane,
                farClip = lens.FarClipPlane,
                dutch = lens.Dutch,
                lensShift = lens.LensShift
            });

            // GML todo: GC allocs? - cache this stuff
            var e = EnsureTargetCompliance(followTarget);
            if (!m.HasComponent<CM_VcamFollowTarget>(Entity))
                m.AddComponentData(Entity, new CM_VcamFollowTarget{ target = e });
            m.SetComponentData(Entity, new CM_VcamFollowTarget{ target = e });

            e = EnsureTargetCompliance(lookAtTarget);
            if (!m.HasComponent<CM_VcamLookAtTarget>(Entity))
                m.AddComponentData(Entity, new CM_VcamLookAtTarget{ target = e });
            m.SetComponentData(Entity, new CM_VcamLookAtTarget{ target = e });
        }
    }
}
