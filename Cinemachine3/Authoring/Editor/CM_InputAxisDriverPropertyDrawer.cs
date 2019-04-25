using UnityEngine;
using UnityEditor;
using System.Reflection;
using Unity.Cinemachine3;
using Unity.Mathematics;
using System;

namespace Cinemachine.Editor.ECS
{
    [CustomPropertyDrawer(typeof(CM_InputAxisDriver))]
    internal sealed class CM_InputAxisDriverPropertyDrawer : PropertyDrawer
    {
        const int vSpace = 2;
        bool mExpanded = false;
        CM_InputAxisDriver def = new CM_InputAxisDriver(); // to access name strings

        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            rect.height = height;

            int oldIndent = EditorGUI.indentLevel;
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            float indentOffset = oldIndent * 15f;
            float w = indentOffset + oldLabelWidth;
            mExpanded = EditorGUI.Foldout(new Rect(rect.x, rect.y, w, rect.height), mExpanded, label, true);

            // Is the axis name valid?
            var nameProp = property.FindPropertyRelative(() => def.name);
            string nameError = string.Empty;
            var nameValue = nameProp.stringValue;
            if (nameValue.Length > 0)
                try { CinemachineCore.GetInputAxis(nameValue); }
                catch (ArgumentException e) { nameError = e.Message; }

            // Draw the input name on the same line as the foldout
            var nameLabel = new GUIContent(nameProp.displayName, nameProp.tooltip);
            EditorGUI.indentLevel = 0;
            EditorGUIUtility.labelWidth = GUI.skin.label.CalcSize(nameLabel).x;
            Rect r = new Rect(rect.x + w, rect.y, rect.width - w, rect.height);
            EditorGUI.PropertyField(r, nameProp, nameLabel);

            // Show an error icon if there's a problem
            if (nameError.Length > 0)
            {
                r.x += r.width - (height + vSpace);
                EditorGUI.LabelField(r, new GUIContent(
                    EditorGUIUtility.IconContent("console.erroricon.sml").image,
                    nameError));
            }

            EditorGUI.indentLevel = oldIndent;
            EditorGUIUtility.labelWidth = oldLabelWidth;

            if (mExpanded)
            {
                ++EditorGUI.indentLevel;

                rect.y += height + vSpace;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative(() => def.multiplier));

                rect.y += height + vSpace;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative(() => def.accelTime));

                rect.y += height + vSpace;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative(() => def.decelTime));

                rect.y += height + vSpace;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative(() => def.inputValue));

                --EditorGUI.indentLevel;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight + vSpace;
            if (mExpanded)
            {
                height *= 5;
            }
            return height - vSpace;
        }
    }
}
