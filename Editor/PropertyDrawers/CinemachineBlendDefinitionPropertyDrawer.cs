using UnityEngine;
using UnityEditor;

namespace Cinemachine.Editor
{
    [CustomPropertyDrawer(typeof(CinemachineBlendDefinitionPropertyAttribute))]
    internal sealed class CinemachineBlendDefinitionPropertyDrawer : PropertyDrawer
    {
        CinemachineBlendDefinition myClass = new CinemachineBlendDefinition(); // to access name strings
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            float floatFieldWidth = EditorGUIUtility.singleLineHeight * 2.5f;

            SerializedProperty timeProp = property.FindPropertyRelative(() => myClass.m_Time);
            GUIContent timeText = new GUIContent(" s", timeProp.tooltip);
            var textDimensions = GUI.skin.label.CalcSize(timeText);

            Rect r = EditorGUI.PrefixLabel(rect, label);

            r.height = EditorGUIUtility.singleLineHeight;
            r.width -= floatFieldWidth + textDimensions.x;

            SerializedProperty styleProp = property.FindPropertyRelative(() => myClass.m_Style);
            EditorGUI.PropertyField(r, styleProp, GUIContent.none);
            if (styleProp.intValue != (int)CinemachineBlendDefinition.Style.Cut)
            {
                float oldWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = textDimensions.x;
                r.x += r.width; r.width = floatFieldWidth + EditorGUIUtility.labelWidth;
                EditorGUI.PropertyField(r, timeProp, timeText);
                timeProp.floatValue = Mathf.Max(timeProp.floatValue, 0);
                EditorGUIUtility.labelWidth = oldWidth;
            }

            if (styleProp.enumValueIndex == (int)CinemachineBlendDefinition.Style.Custom)
            {
                ++EditorGUI.indentLevel;
                SerializedProperty curveProp = property.FindPropertyRelative(() => myClass.m_CustomCurve);
                rect.y += r.height + vSpace;
                rect.height -= r.height + vSpace;
                EditorGUI.PropertyField(rect, curveProp, true);
                --EditorGUI.indentLevel;
            }
        }

        const float vSpace = 2;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight + vSpace;
            SerializedProperty styleProp = property.FindPropertyRelative(() => myClass.m_Style);
            if (styleProp.enumValueIndex == (int)CinemachineBlendDefinition.Style.Custom)
            {
                SerializedProperty curveProp = property.FindPropertyRelative(() => myClass.m_CustomCurve);
                height += vSpace + EditorGUI.GetPropertyHeight(curveProp, true);
            }
            return height;
        }
    }
}
