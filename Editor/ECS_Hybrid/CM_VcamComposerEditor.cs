using UnityEngine;
using UnityEditor;
using Cinemachine.Utility;
using Cinemachine.ECS_Hybrid;
using static Cinemachine.ECS.MathHelpers;
using Cinemachine.ECS;
using Unity.Mathematics;
using Unity.Entities;

namespace Cinemachine.Editor.ECS_Hybrid
{
    [CustomEditor(typeof(CM_VcamComposerComponent))]
    internal class CM_VcamComposerEditor : BaseEditor<CM_VcamComposerComponent>
    {
        CinemachineScreenComposerGuides mScreenGuideEditor;

        protected virtual void OnEnable()
        {
            mScreenGuideEditor = new CinemachineScreenComposerGuides();
            mScreenGuideEditor.GetHardGuide = () => { return ToRect(Target.Value.GetHardGuideRect()); };
            mScreenGuideEditor.GetSoftGuide = () => { return ToRect(Target.Value.GetSoftGuideRect()); };
            mScreenGuideEditor.SetHardGuide = (Rect r) =>
            {
                var v = Target.Value;
                v.SetHardGuideRect(FromRect(r));
                Target.Value = v;
            };
            mScreenGuideEditor.SetSoftGuide = (Rect r) =>
            {
                var v = Target.Value;
                v.SetSoftGuideRect(FromRect(r));
                Target.Value = v;
            };
            mScreenGuideEditor.Target = () => { return serializedObject; };

            CinemachineDebug.OnGUIHandlers -= OnGUI;
            CinemachineDebug.OnGUIHandlers += OnGUI;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static Rect ToRect(rect2d r) { return new Rect(r.pos + new float2(0.5f, 0.5f), r.size); }
        static rect2d FromRect(Rect r) { return new rect2d { pos = r.position - new Vector2(0.5f, 0.5f), size = r.size }; }

        protected virtual void OnDisable()
        {
            CinemachineDebug.OnGUIHandlers -= OnGUI;
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        public override void OnInspectorGUI()
        {
            BeginInspector();

            if (Target.GetEntityComponentData<CM_VcamLookAtTarget>().target == Entity.Null)
                EditorGUILayout.HelpBox(
                    "A LookAt target is required.  Behaviour will be undefined.  Remove this component you don't want a LookAt target.",
                    MessageType.Error);

            // First snapshot some settings
            Rect oldHard = ToRect(Target.Value.GetHardGuideRect());
            Rect oldSoft = ToRect(Target.Value.GetSoftGuideRect());

            // Draw the properties
            DrawRemainingPropertiesInInspector();
            mScreenGuideEditor.SetNewBounds(
                oldHard, oldSoft,
                ToRect(Target.Value.GetHardGuideRect()),
                ToRect(Target.Value.GetSoftGuideRect()));
        }

        protected virtual void OnGUI()
        {
            if (Target == null)
                return;

            // Draw the camera guides
            var vcam = Target.Vcam;
            var brain = vcam == null ? null : CM_Brain.FindBrain(vcam.ParentChannel);
            if (brain == null || brain.OutputCamera == null || !brain.m_ShowGameViewGuides)
                return;

            var entity = Target.Entity;
            bool isLive = brain.VcamIsLive(entity);

            // Screen guides
            var state = CM_EntityVcam.StateFromEntity(entity);
            mScreenGuideEditor.OnGUI_DrawGuides(isLive, brain.OutputCamera, state.Lens, true);

            // Draw an on-screen gizmo for the target
            if (state.HasLookAt && isLive)
            {
                Vector3 targetScreenPosition = brain.OutputCamera.WorldToScreenPoint(state.ReferenceLookAt);
                if (targetScreenPosition.z > 0)
                {
                    targetScreenPosition.y = Screen.height - targetScreenPosition.y;

                    GUI.color = CinemachineSettings.ComposerSettings.TargetColour;
                    Rect r = new Rect(targetScreenPosition, Vector2.zero);
                    float size = (CinemachineSettings.ComposerSettings.TargetSize
                        + CinemachineScreenComposerGuides.kGuideBarWidthPx) / 2;
                    GUI.DrawTexture(r.Inflated(new Vector2(size, size)), Texture2D.whiteTexture);
                    size -= CinemachineScreenComposerGuides.kGuideBarWidthPx;
                    if (size > 0)
                    {
                        Vector4 overlayOpacityScalar
                            = new Vector4(1f, 1f, 1f, CinemachineSettings.ComposerSettings.OverlayOpacity);
                        GUI.color = Color.black * overlayOpacityScalar;
                        GUI.DrawTexture(r.Inflated(new Vector2(size, size)), Texture2D.whiteTexture);
                    }
                }
            }
        }
    }
}
