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
            var brain = CM_Brain.FindBrain(Target.GetEntityComponentData<CM_VcamChannel>().channel);
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

            var brain = CM_Brain.FindBrain(Target.GetEntityComponentData<CM_VcamChannel>().channel);
            Color color = GUI.color;
            bool isSolo = brain != null && brain.SoloCamera == Target.AsEntity;
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
            var brain = CM_Brain.FindBrain(Target.GetEntityComponentData<CM_VcamChannel>().channel);
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
    }
}
