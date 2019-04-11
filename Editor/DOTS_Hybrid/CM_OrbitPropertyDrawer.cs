using UnityEngine;
using UnityEditor;
using Cinemachine.ECS;
using Unity.Mathematics;

namespace Cinemachine.Editor.ECS
{
    [CustomPropertyDrawer(typeof(CM_OrbitPropertyAttribute))]
    internal sealed class CM_OrbitPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
        {
            var def = new CM_VcamOrbital.Orbit();
            InspectorUtility.MultiPropertyOnLine(
                rect, label,
                new [] { property.FindPropertyRelative(() => def.height),
                        property.FindPropertyRelative(() => def.radius) },
                null);
        }
    }
}
