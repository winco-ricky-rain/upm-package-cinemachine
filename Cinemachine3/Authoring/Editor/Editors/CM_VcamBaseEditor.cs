using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Cinemachine;
using Cinemachine.Editor;
using Cinemachine.Utility;
using Unity.Cinemachine.Common.Editor;

namespace Unity.Cinemachine3.Authoring.Editor
{
    /// <summary>
    /// Base class for virtual camera editors.
    /// Handles drawing the header and the basic properties.
    /// </summary>
    [CustomEditor(typeof(CM_VcamBase), false)]
    public class CM_VcamBaseEditor<T> : BaseEditor<T>  where T : CM_VcamBase
    {
        protected int TopLevelChannel { get; private set; }

        protected virtual void OnEnable()
        {
            TopLevelChannel = Target.VirtualCamera.FindTopLevelChannel();
        }

        protected virtual void OnDisable()
        {
            var ch = new ChannelHelper(TopLevelChannel);
            if (ch.SoloCamera == Target.VirtualCamera)
            {
                ch.SoloCamera = VirtualCamera.Null;
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

            var ch = new ChannelHelper(TopLevelChannel);
            Color color = GUI.color;
            bool isSolo = ch.SoloCamera == Target.VirtualCamera && !Target.VirtualCamera.IsNull;
            if (isSolo)
                GUI.color = CM_Brain.GetSoloGUIColor();

            bool isLive = Target.IsLive;
            GUI.enabled = isLive;
            GUI.Label(rectLabel, isLive ? "Status: Live"
                : (Target.isActiveAndEnabled ? "Status: Standby" : "Status: Disabled"));
            GUI.enabled = true;
            GUI.enabled = FindBrain() != null;
            if (GUI.Button(rect, "Solo", "Button"))
            {
                isSolo = !isSolo;
                ch.SoloCamera = isSolo ? Target.VirtualCamera : VirtualCamera.Null;
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
            GUI.enabled = true;
            GUI.color = color;
            if (isSolo && !Application.isPlaying)
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        protected void DrawGlobalControlsInInspector()
        {
            var brain = FindBrain();
            if (brain != null)
                brain.m_ShowGameViewGuides = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Game View Guides",
                        "Enable the display of overlays in the Game view.  You can adjust colours and opacity in Edit/Preferences/Cinemachine."),
                    brain.m_ShowGameViewGuides);

            SaveDuringPlay.Enabled = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Save During Play",
                        "If checked, Virtual Camera settings changes made during Play Mode will be propagated back to the scene when Play Mode is exited."),
                    SaveDuringPlay.Enabled);

            if (Application.isPlaying && SaveDuringPlay.Enabled)
                EditorGUILayout.HelpBox(
                    " Virtual Camera settings changes made during Play Mode will be propagated back to the scene when Play Mode is exited.",
                    MessageType.Info);
        }

        protected CM_Brain FindBrain()
        {
            var ch = new ChannelHelper(TopLevelChannel);
            if (ch.EntityManager.HasComponent<CM_Brain>(ch.Entity))
                return ch.EntityManager.GetComponentObject<CM_Brain>(ch.Entity);
            return null;
        }

        static readonly string kGizmoFileName = "Packages/com.unity.cinemachine/Gizmos/cm_logo.png";
        protected static void DrawStandardVcamGizmos(T vcam, GizmoType drawType)
        {
            var color = vcam.IsLive
                ? CinemachineSettings.CinemachineCoreSettings.ActiveGizmoColour
                : CinemachineSettings.CinemachineCoreSettings.InactiveGizmoColour;
            var state = vcam.State;
            DrawCameraFrustumGizmo(state, color);
            Gizmos.DrawIcon(state.FinalPosition, kGizmoFileName, true);
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
