using UnityEngine;
using UnityEditor;
using Unity.Cinemachine3;
using Unity.Mathematics;

namespace Cinemachine.Editor.ECS
{
    [CustomPropertyDrawer(typeof(CM_RangePropertyAttribute))]
    internal sealed class CM_RangePropertyDrawer : PropertyDrawer
    {
        const int hSpace = 2;
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            var toLabel =  new GUIContent("..");
            float toLabelSize =  GUI.skin.label.CalcSize(toLabel).x + hSpace;

            float w = rect.width - EditorGUIUtility.labelWidth;
            w = (w - toLabelSize - hSpace) / 2;
            if (w > 0)
            {
                var xProp = property.FindPropertyRelative("x");
                var yProp = property.FindPropertyRelative("y");

                rect.width -= w + toLabelSize + hSpace;
                float x = EditorGUI.DelayedFloatField(rect, label, xProp.floatValue);

                rect.x += rect.width + hSpace; rect.width = w + toLabelSize;
                EditorGUI.indentLevel = 0;
                EditorGUIUtility.labelWidth = toLabelSize;
                float y = EditorGUI.DelayedFloatField(rect, toLabel, yProp.floatValue);

                if (xProp.floatValue != x)
                    y = math.max(x, y);
                else if (yProp.floatValue != y)
                    x = math.min(x, y);

                xProp.floatValue = x;
                yProp.floatValue = y;
            }
        }
    }
}
