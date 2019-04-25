using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using Unity.Entities;
using Cinemachine.Editor;

namespace Unity.Cinemachine3.Authoring.Editor
{
    [CustomEditor(typeof(CM_VcamOrbitalProxy))]
    internal class CM_VcamOrbitalEditor : BaseEditor<CM_VcamOrbitalProxy>
    {
        [DrawGizmo(GizmoType.Active | GizmoType.NotInSelectionHierarchy
            | GizmoType.InSelectionHierarchy | GizmoType.Pickable, typeof(CM_VcamOrbitalProxy))]
        static void DrawGizmos(CM_VcamOrbitalProxy orbital, GizmoType selectionType)
        {
            var e = orbital.Entity;
            var m = World.Active?.EntityManager;
            if (m == null || e == Entity.Null || !m.HasComponent<CM_VcamOrbitalState>(e))
                return;

            var state = m.GetComponentData<CM_VcamOrbitalState>(e);
            float scale = orbital.Value.radialAxis.value;
            var pos = state.previousTargetPosition;
            var orient = math.normalizesafe(state.previousTargetRotation);
            var up = math.mul(orient, math.up());

            Color originalGizmoColour = Gizmos.color;
            Gizmos.color = orbital.Vcam.IsLive
                ? CinemachineSettings.CinemachineCoreSettings.ActiveGizmoColour
                : CinemachineSettings.CinemachineCoreSettings.InactiveGizmoColour;

            DrawCircleAtPointWithRadius(
                pos + up * orbital.Value.top.height * scale,
                orient, orbital.Value.top.radius * scale);
            DrawCircleAtPointWithRadius(
                pos + up * orbital.Value.middle.height * scale,
                orient, orbital.Value.middle.radius * scale);
            DrawCircleAtPointWithRadius(
                pos + up * orbital.Value.bottom.height * scale,
                orient, orbital.Value.bottom.radius * scale);

            DrawCameraPath(pos, orient, scale, ref state);

            Gizmos.color = originalGizmoColour;
        }

        static void DrawCircleAtPointWithRadius(float3 point, quaternion orient, float radius)
        {
            Matrix4x4 prevMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(point, orient, radius * Vector3.one);

            const int kNumPoints = 25;
            Vector3 currPoint = Vector3.forward;
            Quaternion rot = Quaternion.AngleAxis(360f / (float)kNumPoints, Vector3.up);
            for (int i = 0; i < kNumPoints + 1; ++i)
            {
                Vector3 nextPoint = rot * currPoint;
                Gizmos.DrawLine(currPoint, nextPoint);
                currPoint = nextPoint;
            }
            Gizmos.matrix = prevMatrix;
        }

        static void DrawCameraPath(
            float3 atPos, quaternion orient, float scale, ref CM_VcamOrbitalState state)
        {
            Matrix4x4 prevMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(atPos, orient, Vector3.one * scale);

            const int kNumSteps = 20;
            Vector3 currPos = state.SplineValueAt(-1f);
            for (int i = 1; i < kNumSteps + 1; ++i)
            {
                float t = ((float)i * 2 / (float)kNumSteps) - 1;
                Vector3 nextPos = state.SplineValueAt(t);
                Gizmos.DrawLine(currPos, nextPos);
                currPos = nextPos;
            }
            Gizmos.matrix = prevMatrix;
        }
    }
}
