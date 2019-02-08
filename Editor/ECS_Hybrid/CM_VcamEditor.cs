using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using Cinemachine.Utility;
using Cinemachine.ECS_Hybrid;
using Cinemachine.ECS;
using System.Reflection;

namespace Cinemachine.Editor.ECS_Hybrid
{
    /// <summary>
    /// Base class for virtual camera editors.
    /// Handles drawing the header and the basic properties.
    /// </summary>
    [CustomEditor(typeof(CM_Vcam))]
    public class CM_VcamEditor : BaseEditor<CM_Vcam>
    {
        ComponentManagerDropdown mBodyDropdown = new ComponentManagerDropdown();
        ComponentManagerDropdown mAimDropdown = new ComponentManagerDropdown();
        ComponentManagerDropdown mNoiseDropdown = new ComponentManagerDropdown();

        protected virtual void OnEnable()
        {
            mBodyDropdown.Initialize(
                new GUIContent("Procedural"),
                new GUIContent("Body", "Procedural algorithm for positioning the camera"),
                "Do Nothing",
                Target.gameObject, CinemachineCore.Stage.Body);
            mAimDropdown.Initialize(
                GUIContent.none,
                new GUIContent("Aim", "Procedural algorithm for orienting camera"),
                "Do Nothing",
                Target.gameObject, CinemachineCore.Stage.Aim);
            mNoiseDropdown.Initialize(
                GUIContent.none, new
                GUIContent("Noise", "Procedural algorithm for adding position or rotation noise"),
                "(none)",
                Target.gameObject, CinemachineCore.Stage.Noise);
        }

        protected virtual void OnDisable()
        {
            if (CinemachineBrain.SoloCamera == (ICinemachineCamera)Target)
            {
                CinemachineBrain.SoloCamera = null;
                // GML is this the right thing to do?
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        public override void OnInspectorGUI()
        {
            BeginInspector();
            DrawHeaderInInspector();
            DrawRemainingPropertiesInInspector();
            mBodyDropdown.DrawExtensionsWidgetInInspector();
            mAimDropdown.DrawExtensionsWidgetInInspector();
            mNoiseDropdown.DrawExtensionsWidgetInInspector();
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
            bool isSolo = (CM_Brain.SoloCamera == (ICinemachineCamera)Target);
            if (isSolo)
                GUI.color = CM_Brain.GetSoloGUIColor();

            bool isLive = brain != null ? brain.VcamIsLive(Target.AsEntity) : false;
            GUI.enabled = isLive;
            GUI.Label(rectLabel, isLive ? "Status: Live"
                : (Target.isActiveAndEnabled ? "Status: Standby" : "Status: Disabled"));
            GUI.enabled = true;

            if (GUI.Button(rect, "Solo", "Button"))
            {
                isSolo = !isSolo;
                CinemachineBrain.SoloCamera = isSolo ? Target : null;
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
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

            SaveDuringPlay.SaveDuringPlay.Enabled
                = EditorGUILayout.Toggle(
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

    class ComponentManagerDropdown
    {
        static Type[] sAllTypes;  // First entry is null
        static string[] sAllNames;

        Type[] myTypes;  // First entry is null
        string[] myNames;

        GameObject mTarget;
        GUIContent mHeader = GUIContent.none;
        GUIContent mLabel = GUIContent.none;
        string mEmptyLabel;
        CinemachineCore.Stage mStageFilter;

        public void Initialize(
            GUIContent header, GUIContent label, string emptyLabel,
            GameObject target,
            CinemachineCore.Stage stageFilter)
        {
            mHeader = header;
            mLabel = label;
            mEmptyLabel = emptyLabel;
            mTarget = target;
            mStageFilter = stageFilter;
            myTypes = null;
            myNames = null;
        }

        public void DrawExtensionsWidgetInInspector()
        {
            RefreshLists();
            RefreshCurrent();
            if (mHeader != GUIContent.none)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(mHeader, EditorStyles.boldLabel);
            }
            Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            rect = EditorGUI.PrefixLabel(rect, mLabel);

            int selection = EditorGUI.Popup(rect, mCurrent, myNames);
            if (selection != mCurrent)
            {
                if (mCurrent != 0)
                {
                    var old = mTarget.GetComponent(myTypes[mCurrent]);
                    if (old != null)
                        Undo.DestroyObjectImmediate(old);
                }
                Type type = myTypes[selection];
                if (type != null)
                    Undo.AddComponent(mTarget.gameObject, type);
                mCurrent = selection;
            }
        }

        void RefreshLists()
        {
            if (sAllTypes == null)
            {
                // Populate the master list
                List<Type> types = new List<Type>();
                List<string> names = new List<string>();
                types.Add(null);
                names.Add(string.Empty);
                var allClasses
                    = ReflectionHelpers.GetTypesInAllDependentAssemblies(
                            (Type t) => t.GetCustomAttribute<CM_PipelineAttribute>() != null);
                foreach (Type t in allClasses)
                {
                    types.Add(t);
                    names.Add(t.Name);
                }
                sAllTypes = types.ToArray();
                sAllNames = names.ToArray();
            }

            if (myTypes == null)
            {
                List<Type> types = new List<Type>();
                List<string> names = new List<string>();
                types.Add(null);
                names.Add(mEmptyLabel);
                for (int i = 1; i < sAllTypes.Length; ++i)
                {
                    var attr = sAllTypes[i].GetCustomAttribute<CM_PipelineAttribute>();
                    if (attr != null && attr.Stage == mStageFilter)
                    {
                        types.Add(sAllTypes[i]);
                        names.Add(sAllTypes[i].Name);
                    }
                }
                myTypes = types.ToArray();
                myNames = names.ToArray();
            }
        }

        List<MonoBehaviour> mBahaviourList = new List<MonoBehaviour>();
        int mCurrent = 0;
        void RefreshCurrent()
        {
            mCurrent = 0;
            mTarget.GetComponents(mBahaviourList);
            for (int i = 0; i < mBahaviourList.Count; ++i)
            {
                var attr = mBahaviourList[i].GetType().GetCustomAttribute<CM_PipelineAttribute>();
                if (attr != null && attr.Stage == mStageFilter)
                {
                    for (int j = 1; j < myTypes.Length; ++j)
                    {
                        if (mBahaviourList[i].GetType() == myTypes[j])
                        {
                            mCurrent = j;
                            return;
                        }
                    }
                }
            }
        }
    }
}
