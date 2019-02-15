using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using Cinemachine.Utility;
using Cinemachine.ECS_Hybrid;
using Cinemachine.ECS;
using System.Reflection;
using Unity.Entities;

namespace Cinemachine.Editor.ECS_Hybrid
{
    /// <summary>
    /// Base class for virtual camera editors.
    /// Handles drawing the header and the basic properties.
    /// </summary>
    [CustomEditor(typeof(CM_Vcam), true)]
    public class CM_VcamEditor : CM_VcamBaseEditor
    {
        ComponentManagerDropdown mBodyDropdown = new ComponentManagerDropdown();
        ComponentManagerDropdown mAimDropdown = new ComponentManagerDropdown();
        ComponentManagerDropdown mNoiseDropdown = new ComponentManagerDropdown();

        protected override void OnEnable()
        {
            base.OnEnable();
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

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            mBodyDropdown.DrawExtensionsWidgetInInspector();
            mAimDropdown.DrawExtensionsWidgetInInspector();
            mNoiseDropdown.DrawExtensionsWidgetInInspector();
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

        public static string NicifyClassName(string name)
        {
            if (name.StartsWith("CM_Vcam"))
                name = name.Substring(7); // Trim the prefix
            if (name.EndsWith("Component"))
                name = name.Substring(0, name.Length-9); // Trim the suffix
            return ObjectNames.NicifyVariableName(name);
        }
    }
}
