using UnityEngine;
using UnityEditor;
using System.Reflection;
using Unity.Cinemachine3;
using Unity.Mathematics;

namespace Cinemachine.Editor.ECS
{
    [CustomPropertyDrawer(typeof(CM_InputAxis))]
    internal sealed class CM_InputAxisPropertyDrawer : PropertyDrawer
    {
        const int vSpace = 2;
        bool mExpanded = false;
        CM_InputAxis def = new CM_InputAxis(); // to access name strings

        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            rect.height = height;

            int oldIndent = EditorGUI.indentLevel;
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            float indentOffset = oldIndent * 15f;
            float w = indentOffset + oldLabelWidth;
            mExpanded = EditorGUI.Foldout(new Rect(rect.x, rect.y, w, rect.height), mExpanded, label, true);

            // Draw the value on the same line as the foldout
            var valueProp = property.FindPropertyRelative(() => def.value);
            var valueLabel = new GUIContent(valueProp.displayName, valueProp.tooltip);
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = GUI.skin.label.CalcSize(valueLabel).x;
            EditorGUI.PropertyField(new Rect(rect.x + w, rect.y, rect.width - w, rect.height), valueProp);

            EditorGUI.indentLevel = oldIndent;
            EditorGUIUtility.labelWidth = oldLabelWidth;
            if (mExpanded)
            {
                ++EditorGUI.indentLevel;

                var rangeProp = property.FindPropertyRelative(() => def.range);
                var recentering = property.FindPropertyRelative(() => def.recentering);
                var centerProp = recentering.FindPropertyRelative(() => def.recentering.center);

                if (!ValueRangeIsLocked(property))
                {
                    rect.y += height + vSpace;
                    InspectorUtility.MultiPropertyOnLine(
                        rect, new GUIContent("Range"),
                        new [] { rangeProp, property.FindPropertyRelative(() => def.wrap) },
                        new [] { GUIContent.none, null });
                }

                rect.y += height + vSpace;
                InspectorUtility.MultiPropertyOnLine(
                    rect, new GUIContent(recentering.displayName, recentering.tooltip),
                    new [] {
                        recentering.FindPropertyRelative(() => def.recentering.enabled),
                        centerProp,
                        recentering.FindPropertyRelative(() => def.recentering.wait),
                        recentering.FindPropertyRelative(() => def.recentering.time)},
                    new [] { new GUIContent(""), new GUIContent("To"), null, null } );

                var xProp = rangeProp.FindPropertyRelative("x");
                var yProp = rangeProp.FindPropertyRelative("y");
                centerProp.floatValue = math.clamp(centerProp.floatValue, xProp.floatValue, yProp.floatValue);

                --EditorGUI.indentLevel;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight + vSpace;
            if (mExpanded)
            {
                int lines = 2;
                if (!ValueRangeIsLocked(property))
                    ++lines;
                height *= lines;
            }
            return height - vSpace;
        }

        bool ValueRangeIsLocked(SerializedProperty property)
        {
            bool value = false;
            PropertyInfo pi = typeof(CM_InputAxis).GetProperty(
                "ValueRangeLocked", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pi != null)
                value = bool.Equals(true, pi.GetValue(SerializedPropertyHelper.GetPropertyValue(property), null));
            return value;
        }
    }
}
