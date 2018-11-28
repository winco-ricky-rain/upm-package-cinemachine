using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Cinemachine.ECS
{
    [UpdateAfter(typeof(CM_VcamCorrectionSystem))]
    public class CM_VcamRaycastShotQualitySystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamShotQuality>(), 
                ComponentType.ReadOnly<CM_VcamPosition>(),
                ComponentType.ReadOnly<CM_VcamRotation>(),
                ComponentType.ReadOnly<CM_VcamLensState>());
        }
       
        [BurstCompile]
        struct SetupRaycastsJob : IJobParallelFor
        {
            public int layerMask;
            public float minDstanceFromTarget;
            public NativeArray<RaycastCommand> raycasts;
            [ReadOnly] public ComponentDataArray<CM_VcamPosition> positions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotation> rotations;

            // GML todo: handle IgnoreTag or something like that ?

            public void Execute(int i)
            {
                // GML todo: check for no lookAt condition
                float3 dir = rotations[i].lookAtPoint - positions[i].raw;
                float distance = math.length(dir);
                raycasts[i] = new RaycastCommand(
                    positions[i].raw, dir / distance, 
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
            [ReadOnly] public ComponentDataArray<CM_VcamRotation> rotations;
            [ReadOnly] public ComponentDataArray<CM_VcamLensState> lenses;

            public void Execute(int i)
            {
                bool isVisible = (hits[i].normal == Vector3.zero)
                    && !IsTargetOnscreen(
                        raycasts[i].from, rotations[i].raw, 
                        lenses[i].fov, aspect, isOrthographic, rotations[i].lookAtPoint);
                qualities[i] = new CM_VcamShotQuality { value = math.select(0f, 1f, isVisible) };
            }
        }
        
        static bool IsTargetOnscreen(
            float3 pos, quaternion rot, float fov, float aspect, bool isOrthographic, float3 targetPos)
        {
            float3 dir = math.mul(math.inverse(rot), targetPos - pos);
            if (isOrthographic)
                return math.abs(dir.y) < fov && math.abs(dir.x) < fov * aspect;

            fov *= 0.5f * math.radians(fov);    // need half-fov in rad
            float angle = MathHelpers.AngleUnit(
                math.normalize(dir.ProjectOntoPlane(new float3(1, 0, 0))), new float3(0, 0, 1));
            if (angle > fov)
                return false;
            fov = math.atan(math.tan(fov) * aspect);
            angle = MathHelpers.AngleUnit(
                math.normalize(dir.ProjectOntoPlane(math.up())), new float3(0, 0, 1));
            if (angle > fov)
                return false;

            return true;
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
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPosition>(),
                rotations = m_mainGroup.GetComponentDataArray<CM_VcamRotation>(),
            };

            var setupDependency = setupRaycastsJob.Schedule(objectCount, 32, inputDeps);
            var raycastDependency = RaycastCommand.ScheduleBatch(
                raycastCommands, raycastHits, 32, setupDependency);

            var qualityJob = new CalculateQualityJob()
            {
                qualities = m_mainGroup.GetComponentDataArray<CM_VcamShotQuality>(),
                hits = raycastHits,         // deallocates on completion
                raycasts = raycastCommands, // deallocates on completion
                rotations = m_mainGroup.GetComponentDataArray<CM_VcamRotation>(),
                lenses = m_mainGroup.GetComponentDataArray<CM_VcamLensState>(),
            };

            return qualityJob.Schedule(objectCount, 32, raycastDependency);
        }
    }
}
