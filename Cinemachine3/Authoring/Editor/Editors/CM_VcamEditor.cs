using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Cinemachine.Common.Editor;

namespace Unity.Cinemachine3.Authoring.Editor
{
    /// <summary>
    /// Base class for virtual camera editors.
    /// Handles drawing the header and the basic properties.
    /// </summary>
    [CustomEditor(typeof(CM_Vcam), true)]
    public class CM_VcamEditor : CM_VcamBaseEditor<CM_Vcam>
    {
        ComponentManagerDropdown mBodyDropdown = new ComponentManagerDropdown();
        ComponentManagerDropdown mAimDropdown = new ComponentManagerDropdown();
        ComponentManagerDropdown mNoiseDropdown = new ComponentManagerDropdown();
        GUIContent mProcedurlaHeader;

        protected override void OnEnable()
        {
            base.OnEnable();
            mProcedurlaHeader = new GUIContent("Procedural");
            mBodyDropdown.Initialize(
                new GUIContent("Body", "Procedural algorithm for positioning the camera"),
                "Do Nothing",
                Target.gameObject, PipelineStage.Body);
            mAimDropdown.Initialize(
                new GUIContent("Aim", "Procedural algorithm for orienting camera"),
                "Do Nothing",
                Target.gameObject, PipelineStage.Aim);
            mNoiseDropdown.Initialize(
                new GUIContent("Noise", "Procedural algorithm for adding position or rotation noise"),
                "None",
                Target.gameObject, PipelineStage.Noise);
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(mProcedurlaHeader, EditorStyles.boldLabel);

            mBodyDropdown.DrawDropdownWidgetInInspector();
            mAimDropdown.DrawDropdownWidgetInInspector();
            mNoiseDropdown.DrawDropdownWidgetInInspector();
        }

        [DrawGizmo(GizmoType.Selected, typeof(CM_Vcam))]
        static void DrawVcamGizmos(CM_Vcam vcam, GizmoType drawType)
        {
            DrawStandardVcamGizmos(vcam, drawType);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected, typeof(CM_Brain))]
        private static void DrawBrainGizmos(CM_Brain brain, GizmoType drawType)
        {
            if (brain.m_ShowCameraFrustum)
                DrawCameraFrustumGizmo(brain.CameraState, Color.white); // GML why is this color hardcoded?
        }

        public class ComponentManagerDropdown
        {
            static Type[] sAllTypes;  // First entry is null
            static string[] sAllNames;

            Type[] myTypes;  // First entry is null
            string[] myNames;

            GameObject mTarget;
            GUIContent mLabel = GUIContent.none;
            string mEmptyLabel;
            PipelineStage mStageFilter;

            public void Initialize(
                GUIContent label, string emptyLabel,
                GameObject target,
                PipelineStage stageFilter)
            {
                mLabel = label;
                mEmptyLabel = emptyLabel;
                mTarget = target;
                mStageFilter = stageFilter;
                myTypes = null;
                myNames = null;
            }

            public void DrawDropdownWidgetInInspector()
            {
                RefreshLists();
                RefreshCurrent();
                Rect rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                rect = EditorGUI.PrefixLabel(rect, mLabel);

                int selection = EditorGUI.Popup(rect, mCurrent, myNames);
                if (selection != mCurrent)
                {
                    Type type = myTypes[selection];
                    if (type != null)
                        Undo.AddComponent(mTarget.gameObject, type);
                    if (mCurrent != 0)
                    {
                        var old = mTarget.GetComponent(myTypes[mCurrent]);
                        if (old != null)
                        {
                            Undo.DestroyObjectImmediate(old);
                            GUIUtility.ExitGUI();
                        }
                    }
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
                        names.Add(NicifyClassName(t.Name));
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
                            names.Add(sAllNames[i]);
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
                    if (mBahaviourList[i] == null || mBahaviourList[i].GetType() == null)
                        continue;
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

            static string NicifyClassName(string name)
            {
                if (name.StartsWith("CM_Vcam"))
                    name = name.Substring(7); // Trim the prefix
                if (name.EndsWith("Proxy"))
                    name = name.Substring(0, name.Length-5); // Trim the suffix
                return ObjectNames.NicifyVariableName(name);
            }
        }
    }
}
