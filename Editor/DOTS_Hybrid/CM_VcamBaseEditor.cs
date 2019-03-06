using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Cinemachine.Utility;
using Cinemachine.ECS_Hybrid;
using Cinemachine.ECS;
using Unity.Entities;

namespace Cinemachine.Editor.ECS_Hybrid
{
    /// <summary>
    /// Base class for virtual camera editors.
    /// Handles drawing the header and the basic properties.
    /// </summary>
    [CustomEditor(typeof(CM_VcamBase), true)]
    public class CM_VcamBaseEditor : BaseEditor<CM_VcamBase>
    {
        protected virtual void OnEnable()
        {
        }

        protected virtual void OnDisable()
        {
            var brain = Target == null ? null : CM_Brain.FindBrain(Target.ParentChannel);
            if (brain != null && brain.SoloCamera == Target.AsEntity)
            {
                brain.SoloCamera = Entity.Null;
                // GML is this the right thing to do?
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        public override void OnInspectorGUI()
        {
            BeginInspector();
            DrawHeaderInInspector();
            DrawRemainingPropertiesInInspector();
        }

        protected void DrawHeaderInInspector()
        {
            List<string> excluded = GetExcludedPropertiesInInspector();
            if (!excluded.Contains("Header"))
            {
                DrawCameraStatusInInspector();
                DrawGlobalControlsInInspector();
            }
            ExcludeProperty("Header");
        }

        protected void DrawCameraStatusInInspector()
        {
            // Is the camera navel-gazing?
            CameraState state = Target.State;
            if (state.HasLookAt && (state.ReferenceLookAt - state.CorrectedPosition).AlmostZero())
                EditorGUILayout.HelpBox(
                    "The camera is positioned on the same point at which it is trying to look.",
                    MessageType.Warning);

            // Active status and Solo button
            Rect rect = EditorGUILayout.GetControlRect(true);
            Rect rectLabel = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);
            rect.width -= rectLabel.width;
            rect.x += rectLabel.width;

            var brain = CM_Brain.FindBrain(Target.ParentChannel);
            Color color = GUI.color;
            bool isSolo = brain != null && brain.SoloCamera == Target.AsEntity && Target.AsEntity != Entity.Null;
            if (isSolo)
                GUI.color = CM_Brain.GetSoloGUIColor();

            bool isLive = brain != null ? brain.VcamIsLive(Target.AsEntity) : false;
            GUI.enabled = isLive;
            GUI.Label(rectLabel, isLive ? "Status: Live"
                : (Target.isActiveAndEnabled ? "Status: Standby" : "Status: Disabled"));
            GUI.enabled = true;
            GUI.enabled = brain != null;
            if (GUI.Button(rect, "Solo", "Button"))
            {
                isSolo = !isSolo;
                brain.SoloCamera = isSolo ? Target.AsEntity : Entity.Null;
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            GUI.enabled = true;
            GUI.color = color;
            if (isSolo && !Application.isPlaying)
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        protected void DrawGlobalControlsInInspector()
        {
            var brain = CM_Brain.FindBrain(Target.ParentChannel);
            if (brain != null)
                brain.m_ShowGameViewGuides = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Game Window Guides",
                        "Enable the display of overlays in the Game window.  You can adjust colours and opacity in Edit/Preferences/Cinemachine."),
                    brain.m_ShowGameViewGuides);

            SaveDuringPlay.SaveDuringPlay.Enabled = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Save During Play",
                        "If checked, Virtual Camera settings changes made during Play Mode will be propagated back to the scene when Play Mode is exited."),
                    SaveDuringPlay.SaveDuringPlay.Enabled);

            if (Application.isPlaying && SaveDuringPlay.SaveDuringPlay.Enabled)
                EditorGUILayout.HelpBox(
                    " Virtual Camera settings changes made during Play Mode will be propagated back to the scene when Play Mode is exited.",
                    MessageType.Info);
        }

#if UNITY_2019_1_OR_NEWER
        static readonly string kGizmoFileName = "Packages/com.unity.cinemachine/Gizmos/cm_logo.png";
#else
        static readonly string kGizmoFileName = "Cinemachine/cm_logo_lg.png";
#endif
        [DrawGizmo(GizmoType.Selected, typeof(CM_VcamBase))]
        private static void DrawVcamGizmos(CM_VcamBase vcam, GizmoType drawType)
        {
            var color = vcam.IsLive
                ? CinemachineSettings.CinemachineCoreSettings.ActiveGizmoColour
                : CinemachineSettings.CinemachineCoreSettings.InactiveGizmoColour;
            var state = vcam.State;
            DrawCameraFrustumGizmo(state, color);
            Gizmos.DrawIcon(state.FinalPosition, kGizmoFileName, true);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected, typeof(CM_Brain))]
        private static void DrawBrainGizmos(CM_Brain brain, GizmoType drawType)
        {
            if (brain.m_ShowCameraFrustum)
                DrawCameraFrustumGizmo(brain.CurrentCameraState, Color.white); // GML why is this color hardcoded?
        }

        internal static void DrawCameraFrustumGizmo(CameraState state, Color color)
        {
            var lens = state.Lens;
            Matrix4x4 originalMatrix = Gizmos.matrix;
            Color originalGizmoColour = Gizmos.color;
            Gizmos.color = color;
            Gizmos.matrix = Matrix4x4.TRS(state.FinalPosition, state.FinalOrientation, Vector3.one);
            if (lens.Orthographic)
            {
                Vector3 size = new Vector3(
                        lens.Aspect * lens.OrthographicSize * 2,
                        lens.OrthographicSize * 2,
                        lens.NearClipPlane + lens.FarClipPlane);
                Gizmos.DrawWireCube(
                    new Vector3(0, 0, (size.z / 2) + lens.NearClipPlane), size);
            }
            else
            {
                Gizmos.DrawFrustum(
                        Vector3.zero, lens.FieldOfView,
                        lens.FarClipPlane, lens.NearClipPlane, lens.Aspect);
            }
            Gizmos.matrix = originalMatrix;
            Gizmos.color = originalGizmoColour;
        }
    }
}
