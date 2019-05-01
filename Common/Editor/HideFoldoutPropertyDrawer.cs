using UnityEditor;
using UnityEngine;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine.Editor
{
    [CustomPropertyDrawer(typeof(HideFoldoutAttribute))]
    sealed class HideFoldoutPropertyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = 0;
            var childProperty = property.Copy();
            var endProperty = childProperty.GetEndProperty();
            childProperty.NextVisible(true);
            while (!SerializedProperty.EqualContents(childProperty, endProperty))
            {
                height += EditorGUI.GetPropertyHeight(childProperty)
                    + EditorGUIUtility.standardVerticalSpacing;
                childProperty.NextVisible(false);
            }
            return height - EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var childProperty = property.Copy();
            var endProperty = childProperty.GetEndProperty();
            childProperty.NextVisible(true);
            while (!SerializedProperty.EqualContents(childProperty, endProperty))
            {
                position.height = EditorGUI.GetPropertyHeight(childProperty);
                EditorGUI.PropertyField(position, childProperty, true);
                position.y += position.height + EditorGUIUtility.standardVerticalSpacing;
                childProperty.NextVisible(false);
            }
        }
    }
}
