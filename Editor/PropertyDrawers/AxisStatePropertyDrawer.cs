using UnityEngine;
using UnityEditor;
using System.Reflection;
using System;

namespace Cinemachine.Editor
{
    [CustomPropertyDrawer(typeof(AxisStatePropertyAttribute))]
    internal sealed class AxisStatePropertyDrawer : PropertyDrawer
    {
        const int vSpace = 2;
        bool mExpanded = true;
        AxisState def = new AxisState(); // to access name strings

        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            rect.height = height;
            mExpanded = EditorGUI.Foldout(rect, mExpanded, label, true);
            if (mExpanded)
            {
                ++EditorGUI.indentLevel;

                rect.y += height + vSpace;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative(() => def.Value));

                if (!ValueRangeIsLocked(property))
                {
                    rect.y += height + vSpace;
                    InspectorUtility.MultiPropertyOnLine(rect, new GUIContent("Value Range"),
                        new [] { property.FindPropertyRelative(() => def.m_MinValue),
                                property.FindPropertyRelative(() => def.m_MaxValue),
                                property.FindPropertyRelative(() => def.m_Wrap) },
                        new [] { GUIContent.none, new GUIContent("to "), null });
                }

                rect.y += height + vSpace;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative(() => def.m_MaxSpeed));

                rect.y += height + vSpace;
                InspectorUtility.MultiPropertyOnLine(
                    rect, null,
                    new [] { property.FindPropertyRelative(() => def.m_AccelTime),
                            property.FindPropertyRelative(() => def.m_DecelTime)},
                    new [] { GUIContent.none, null });

                if (HasRecentering(property))
                {
                    var rDef = new AxisState.Recentering();
                    var recentering = property.FindPropertyRelative(() => def.m_Recentering);
                    rect.y += height + vSpace;
                    InspectorUtility.MultiPropertyOnLine(
                        rect, new GUIContent(recentering.displayName, recentering.tooltip),
                        new [] {
                                recentering.FindPropertyRelative(() => rDef.m_enabled),
                                recentering.FindPropertyRelative(() => rDef.m_WaitTime),
                                recentering.FindPropertyRelative(() => rDef.m_RecenteringTime)},
                        new [] { new GUIContent(""),
                                new GUIContent("Wait"),
                                new GUIContent("Time")} );
                }

                var axisName = property.FindPropertyRelative(() => def.m_InputAxisName);
                bool axisIsValid = true;
                var nameValue = axisName.stringValue;
                if (nameValue.Length > 0)
                    try { CinemachineCore.GetInputAxis(nameValue); }
                    catch (ArgumentException) { axisIsValid = false; }

                rect.y += height + vSpace;
                EditorGUI.PropertyField(rect, axisName);
                if (!axisIsValid)
                {
                    Rect r = rect;
                    r.x += r.width - (2 * height + vSpace);
                    EditorGUI.LabelField(r, EditorGUIUtility.IconContent("console.erroricon.sml"));
                }

                rect.y += height + vSpace;
                InspectorUtility.MultiPropertyOnLine(rect, null,
                    new [] { property.FindPropertyRelative(() => def.m_InputAxisValue),
                            property.FindPropertyRelative(() => def.m_InvertInput) },
                    new [] { GUIContent.none, new GUIContent("Invert") });

                --EditorGUI.indentLevel;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight + vSpace;
            if (mExpanded)
            {
                int lines = 6;
                if (!ValueRangeIsLocked(property))
                    ++lines;
                if (HasRecentering(property))
                    ++lines;
                height *= lines;
            }
            return height - vSpace;
        }

        bool ValueRangeIsLocked(SerializedProperty property)
        {
            bool value = false;
            PropertyInfo pi = typeof(AxisState).GetProperty(
                "ValueRangeLocked", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pi != null)
                value = bool.Equals(true, pi.GetValue(SerializedPropertyHelper.GetPropertyValue(property), null));
            return value;
        }

        bool HasRecentering(SerializedProperty property)
        {
            bool value = false;
            PropertyInfo pi = typeof(AxisState).GetProperty(
                "HasRecentering", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (pi != null)
                value = bool.Equals(true, pi.GetValue(SerializedPropertyHelper.GetPropertyValue(property), null));
            return value;
        }
    }
}
