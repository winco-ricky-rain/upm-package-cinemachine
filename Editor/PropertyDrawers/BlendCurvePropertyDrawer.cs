using UnityEngine;
using UnityEditor;
using Cinemachine.ECS;

namespace Cinemachine.Editor
{
    [CustomPropertyDrawer(typeof(BlendCurvePropertyAttribute))]
    internal sealed class BlendCurvePropertyDrawer : PropertyDrawer
    {
        BlendCurve myClass = new BlendCurve { A = 0, B = 0, bias = 0 }; // to access name strings
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            Rect r = rect;
            if (property.isExpanded)
                r.width = r.height = EditorGUIUtility.singleLineHeight;
            property.isExpanded = EditorGUI.Foldout(
                r, property.isExpanded, property.isExpanded ? GUIContent.none : label);
            if (property.isExpanded)
            {
                var propA = property.FindPropertyRelative(() => myClass.A);
                var propB = property.FindPropertyRelative(() => myClass.B);
                var propBias = property.FindPropertyRelative(() => myClass.bias);

                r = rect; r.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(r, propA); r.y += r.height + vSpace;
                EditorGUI.PropertyField(r, propB); r.y += r.height + vSpace;
                EditorGUI.PropertyField(r, propBias); r.y += r.height + vSpace;

                rect.height -= r.y - rect.y; rect.y += r.y - rect.y;
                DrawSample(rect, new BlendCurve
                {
                    A = propA.floatValue,
                    B = propB.floatValue,
                    bias = propBias.floatValue
                });
            }
        }

        Vector3[] mSamples;
        void DrawSample(Rect r, BlendCurve curve)
        {
            // Resample
            int numSamples = (int)(r.width / 2) + 1;
            if (mSamples == null || mSamples.Length != numSamples)
                mSamples = new Vector3[numSamples];
            for (int i = 0; i < numSamples; ++i)
            {
                float x = (float)i / (float)(numSamples - 1);
                float y = 1 - curve.Evaluate(x);
                mSamples[i] = new Vector3(x * r.width, y * r.height, 0);
            }

            // Draw
            EditorGUI.DrawRect(r, Color.black);
            var oldMatrix = Handles.matrix;
            Handles.matrix = Handles.matrix * Matrix4x4.Translate(r.position);
            Handles.color = new Color(0, 1, 0, 1);
            Handles.DrawPolyLine(mSamples);
            Handles.matrix = oldMatrix;
        }

        const float vSpace = 2;
        const float kPreviewHeight = 200;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight + vSpace;
            if (property.isExpanded)
                height += (height + vSpace) * 3 + kPreviewHeight;
            return height;
        }
    }
}
