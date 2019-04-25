using UnityEngine;
using UnityEditor;
using Unity.Cinemachine.Common.Editor;

namespace Unity.Cinemachine3.Authoring.Editor
{
    [CustomEditor(typeof(CM_FreeLook), true)]
    public class CM_FreeLookEditor : CM_VcamBaseEditor<CM_FreeLook>
    {
        CM_VcamEditor.ComponentManagerDropdown mBodyDropdown = new CM_VcamEditor.ComponentManagerDropdown();
        CM_VcamEditor.ComponentManagerDropdown mAimDropdown = new CM_VcamEditor.ComponentManagerDropdown();
        CM_VcamEditor.ComponentManagerDropdown mNoiseDropdown = new CM_VcamEditor.ComponentManagerDropdown();

        GUIContent mProceduralHeader = new GUIContent("Procedural");
        GUIContent mAllLensLabel = new GUIContent(
            "Customize", "Custom settings for this rig.  If unchecked, main rig settins will be used");

        protected override void OnEnable()
        {
            base.OnEnable();

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
            BeginInspector();
            SerializedProperty topRigProp = FindAndExcludeProperty(x => x.topRig);
            SerializedProperty bottomRigProp = FindAndExcludeProperty(x => x.bottomRig);

            DrawHeaderInInspector();
            DrawRemainingPropertiesInInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(mProceduralHeader, EditorStyles.boldLabel);

            mBodyDropdown.DrawDropdownWidgetInInspector();
            mAimDropdown.DrawDropdownWidgetInInspector();
            mNoiseDropdown.DrawDropdownWidgetInInspector();

            var defaults = new CM_FreeLookRigBlendableSettings();
            defaults.PullFrom(Target.AsEntity);
            DrawRigEditor(topRigProp, defaults);
            DrawRigEditor(bottomRigProp, defaults);
        }

        void DrawRigEditor(SerializedProperty rig, CM_FreeLookRigBlendableSettings defaults)
        {
            CM_FreeLookRigBlendableSettings def = new CM_FreeLookRigBlendableSettings(); // for properties

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(rig.displayName, EditorStyles.boldLabel);

            if (DrawFoldoutPropertyWithEnabledCheckbox(
                rig.FindPropertyRelative(() => def.customLens),
                rig.FindPropertyRelative(() => def.lens)))
            {
                // GML todo: set lens to default value
            }

            if (DrawFoldoutPropertyWithEnabledCheckbox(
                rig.FindPropertyRelative(() => def.customBody),
                rig.FindPropertyRelative(() => def.body)))
            {
                // GML todo: set body to default value
            }

            if (DrawFoldoutPropertyWithEnabledCheckbox(
                rig.FindPropertyRelative(() => def.customAim),
                rig.FindPropertyRelative(() => def.aim)))
            {
                // GML todo: set aim to default value
            }

            if (DrawFoldoutPropertyWithEnabledCheckbox(
                rig.FindPropertyRelative(() => def.customNoise),
                rig.FindPropertyRelative(() => def.noise)))
            {
                // GML todo: set noise to default value
            }
        }

        // Returns true if default value should be applied
        bool DrawFoldoutPropertyWithEnabledCheckbox(
            SerializedProperty enabledProperty, SerializedProperty property)
        {
            GUIContent label = new GUIContent(property.displayName, property.tooltip);
            Rect rect = EditorGUILayout.GetControlRect(true,
                (enabledProperty.boolValue && property.isExpanded)
                    ? EditorGUI.GetPropertyHeight(property)
                        : EditorGUIUtility.singleLineHeight);
            Rect r = rect; r.height = EditorGUIUtility.singleLineHeight;
            if (!enabledProperty.boolValue)
                EditorGUI.LabelField(r, label);

            float labelWidth = EditorGUIUtility.labelWidth + 15; // GML wtf 15?
            bool newValue = EditorGUI.ToggleLeft(
                new Rect(labelWidth, r.y, r.width - labelWidth, r.height),
                mAllLensLabel, enabledProperty.boolValue);
            if (newValue != enabledProperty.boolValue)
            {
                enabledProperty.boolValue = newValue;
                enabledProperty.serializedObject.ApplyModifiedProperties();
                property.isExpanded = newValue;
                return true;
            }
            if (newValue == true)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(rect, property, property.isExpanded);
                if (EditorGUI.EndChangeCheck())
                    enabledProperty.serializedObject.ApplyModifiedProperties();
            }
            return false;
        }

        [DrawGizmo(GizmoType.Selected, typeof(CM_FreeLook))]
        static void DrawVcamGizmos(CM_FreeLook vcam, GizmoType drawType)
        {
            DrawStandardVcamGizmos(vcam, drawType);
        }
    }
}
