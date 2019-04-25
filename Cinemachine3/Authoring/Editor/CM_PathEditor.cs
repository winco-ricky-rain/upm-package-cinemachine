using UnityEditor;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Cinemachine.Editor;

namespace Unity.Cinemachine3.Authoring.Editor
{
    [CustomEditor(typeof(CM_PathProxy))]
    internal class CM_PathEditor : BaseEditor<CM_PathProxy>
    {
        public override void OnInspectorGUI()
        {
            BeginInspector();

            float pathLength = 0;
            var m = World.Active?.EntityManager;
            if (m != null)
            {
                if (m.HasComponent<CM_PathState>(Target.Entity))
                    pathLength = m.GetComponentData<CM_PathState>(Target.Entity).PathLength;
            }
            EditorGUILayout.LabelField(new GUIContent("Path length"), new GUIContent(pathLength.ToString()));

            DrawRemainingPropertiesInInspector();
        }

        internal static void DrawPathGizmo(
            CM_PathState pathState, DynamicBuffer<CM_PathWaypointElement> waypoints,
            Color pathColor, float width, int resolution)
        {
            bool looped = pathState.looped;

            // Draw the path
            Color colorOld = Gizmos.color;
            Gizmos.color = pathColor;
            float step = 1f / math.max(1, resolution);
            float3 right = new float3(1, 0, 0) * width / 2;
            float3 lastPos = CM_PathSystem.EvaluatePosition(0, ref pathState, ref waypoints);
            float3 lastW = math.mul(CM_PathSystem.EvaluateOrientation(
                0, ref pathState, ref waypoints), right);

            int count = waypoints.Length;
            float maxPos = math.select(0, math.select(count - 1, count, looped), count > 1);
            for (float t = step; t <= maxPos + step / 2; t += step)
            {
                float3 p = CM_PathSystem.EvaluatePosition(t, ref pathState, ref waypoints);
                quaternion q = CM_PathSystem.EvaluateOrientation(t, ref pathState, ref waypoints);
                float3 w = math.mul(q, right);
                float3 w2 = w * 1.2f;
                float3 p0 = p - w2;
                float3 p1 = p + w2;
                Gizmos.DrawLine(p0, p1);
                Gizmos.DrawLine(lastPos - lastW, p - w);
                Gizmos.DrawLine(lastPos + lastW, p + w);
#if false
                // Show the normals, for debugging
                Gizmos.color = Color.red;
                float3 y = math.mul(ql2w, math.mul(q, math.up()) * width / 2);
                Gizmos.DrawLine(p, p + y);
                Gizmos.color = pathColor;
#endif
                lastPos = p;
                lastW = w;
            }
            Gizmos.color = colorOld;
        }

        [DrawGizmo(GizmoType.Active | GizmoType.NotInSelectionHierarchy
            | GizmoType.InSelectionHierarchy | GizmoType.Pickable, typeof(CM_PathProxy))]
        static void DrawGizmos(CM_PathProxy path, GizmoType selectionType)
        {
            var m = World.Active?.EntityManager;
            if (m == null || !m.HasComponent<CM_PathState>(path.Entity)
                    || !m.HasComponent<CM_PathWaypointElement>(path.Entity))
                return;

            DrawPathGizmo(
                m.GetComponentData<CM_PathState>(path.Entity),
                m.GetBuffer<CM_PathWaypointElement>(path.Entity),
                (Selection.activeGameObject == path.gameObject)
                    ? path.appearance.pathColor : path.appearance.inactivePathColor,
                path.appearance.width, path.Value.resolution);
        }
    }
}

