using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Cinemachine.ECS
{
    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    [UpdateBefore(typeof(CM_VcamPrioritySystem))]
    public class CM_VcamRaycastShotQualitySystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamShotQuality>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamLensState>());
        }

        [BurstCompile]
        struct SetupRaycastsJob : IJobParallelFor
        {
            public int layerMask;
            public float minDstanceFromTarget;
            public NativeArray<RaycastCommand> raycasts;
            [ReadOnly] public ComponentDataArray<CM_VcamPositionState> positions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotationState> rotations;

            // GML todo: handle IgnoreTag or something like that ?

            public void Execute(int i)
            {
                // GML todo: check for no lookAt condition

                // cast back towards the camera to filter out target's collider
                float3 dir = positions[i].raw - rotations[i].lookAtPoint;
                float distance = math.length(dir);
                dir /= distance;
                raycasts[i] = new RaycastCommand(
                    rotations[i].lookAtPoint + minDstanceFromTarget * dir, dir,
                    math.max(0, distance - minDstanceFromTarget), layerMask);
            }
        }

        [BurstCompile]
        struct CalculateQualityJob : IJobParallelFor
        {
            public bool isOrthographic;
            public float aspect;
            public ComponentDataArray<CM_VcamShotQuality> qualities;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastHit> hits;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastCommand> raycasts;
            [ReadOnly] public ComponentDataArray<CM_VcamPositionState> positions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotationState> rotations;
            [ReadOnly] public ComponentDataArray<CM_VcamLensState> lenses;

            public void Execute(int i)
            {
                bool noObstruction = hits[i].normal == Vector3.zero;

                float3 offset = rotations[i].lookAtPoint - positions[i].raw; // GML todo: use corrected
                offset = math.mul(math.inverse(rotations[i].raw), offset); // camera-space
                var fov = lenses[i].fov;
                bool isOnscreen =
                    (!isOrthographic & IsTargetOnscreen(offset, fov, aspect))
                    | (isOrthographic & IsTargetOnscreenOrtho(offset, fov, aspect));

                bool isVisible = noObstruction && isOnscreen;
                qualities[i] = new CM_VcamShotQuality { value = math.select(0f, 1f, isVisible) };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsTargetOnscreen(float3 dir, float size, float aspect)
        {
            float fovY = 0.5f * math.radians(size);    // size is fovH in deg.  need half-fov in rad
            float2 fov = new float2(math.atan(math.tan(fovY) * aspect), fovY);
            float2 angle = new float2(
                MathHelpers.AngleUnit(
                    math.normalize(dir.ProjectOntoPlane(math.up())), new float3(0, 0, 1)),
                MathHelpers.AngleUnit(
                    math.normalize(dir.ProjectOntoPlane(new float3(1, 0, 0))), new float3(0, 0, 1)));
            return math.all(angle <= fov);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsTargetOnscreenOrtho(float3 dir, float size, float aspect)
        {
            float2 s = new float2(size * aspect, size);
            return math.all(math.abs(new float2(dir.x, dir.y)) < s);
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // GML todo: should use corrected position/orientation

            var objectCount = m_mainGroup.CalculateLength();

            // These will be deallocated by the final job
            var raycastCommands = new NativeArray<RaycastCommand>(objectCount, Allocator.TempJob);
            var raycastHits = new NativeArray<RaycastHit>(objectCount, Allocator.TempJob);

            var setupRaycastsJob = new SetupRaycastsJob()
            {
                layerMask = -5, // GML todo: how to set this?
                minDstanceFromTarget = 0, // GML todo: how to set this?
                raycasts = raycastCommands,
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPositionState>(),
                rotations = m_mainGroup.GetComponentDataArray<CM_VcamRotationState>(),
            };

            var setupDependency = setupRaycastsJob.Schedule(objectCount, 32, inputDeps);
            var raycastDependency = RaycastCommand.ScheduleBatch(
                raycastCommands, raycastHits, 32, setupDependency);

            var qualityJob = new CalculateQualityJob()
            {
                isOrthographic = false, // GML fixme
                aspect = (float)Screen.width / (float)Screen.height, // GML fixme
                qualities = m_mainGroup.GetComponentDataArray<CM_VcamShotQuality>(),
                hits = raycastHits,         // deallocates on completion
                raycasts = raycastCommands, // deallocates on completion
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPositionState>(),
                rotations = m_mainGroup.GetComponentDataArray<CM_VcamRotationState>(),
                lenses = m_mainGroup.GetComponentDataArray<CM_VcamLensState>(),
            };

            return qualityJob.Schedule(objectCount, 32, raycastDependency);
        }
    }
}
