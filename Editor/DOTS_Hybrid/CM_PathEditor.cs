using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Cinemachine.ECS;
using UnityEditorInternal;
using Cinemachine.ECS_Hybrid;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.Editor.ECS_Hybrid
{
    [CustomEditor(typeof(CM_PathProxy))]
    internal class CM_PathEditor : BaseEditor<CM_PathProxy>
    {
        internal static void DrawPathGizmo(
            CM_Path path, DynamicBuffer<CM_PathWaypointElement> waypoints,
            Color pathColor, float width, float4x4 l2w)
        {
            quaternion ql2w = l2w.GetRotationFromTRS();

            // Draw the path
            Color colorOld = Gizmos.color;
            Gizmos.color = pathColor;
            float step = 1f / math.max(1, path.resolution);
            float3 right = new float3(1, 0, 0) * width / 2;
            float3 lastPos = math.transform(l2w, CM_PathSystem.EvaluatePosition(0, path, waypoints));
            float3 lastW = math.mul(
                ql2w, math.mul(CM_PathSystem.EvaluateOrientation(0, path, waypoints), right));

            int count = waypoints.Length;
            float maxPos = math.select(0, math.select(count - 1, count, path.looped), count > 1);
            for (float t = step; t <= maxPos + step / 2; t += step)
            {
                float3 p = math.transform(l2w, CM_PathSystem.EvaluatePosition(t, path, waypoints));
                quaternion q = CM_PathSystem.EvaluateOrientation(t, path, waypoints);
                float3 w = math.mul(ql2w, math.mul(q, right));
                float3 w2 = w * 1.2f;
                float3 p0 = p - w2;
                float3 p1 = p + w2;
                Gizmos.DrawLine(p0, p1);
                Gizmos.DrawLine(lastPos - lastW, p - w);
                Gizmos.DrawLine(lastPos + lastW, p + w);
#if true
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
            if (m == null)
                return;

            DrawPathGizmo(path.Value, m.GetBuffer<CM_PathWaypointElement>(path.Entity),
                (Selection.activeGameObject == path.gameObject)
                    ? path.appearance.pathColor : path.appearance.inactivePathColor,
                path.appearance.width, path.transform.localToWorldMatrix);
        }
    }
}

