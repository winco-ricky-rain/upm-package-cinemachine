using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Burst;
using Unity.Mathematics;
using System;

namespace Cinemachine.ECS
{
    // These systems define the CM Vcam pipeline, in this order.
    // Use them to ensure correct ordering of CM pipeline systems

    [UnityEngine.ExecuteInEditMode]
    [UpdateAfter(typeof(CM_TargetSystem))]
    public class CM_VcamPreBodySystem : JobComponentSystem
    {
        ComponentGroup m_posGroup;
        ComponentGroup m_rotGroup;

        protected override void OnCreateManager()
        {
            m_posGroup = GetComponentGroup(ComponentType.Create<CM_VcamPosition>());
            m_rotGroup = GetComponentGroup(ComponentType.Create<CM_VcamRotation>());
        }

        [BurstCompile]
        struct InitPosJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamPosition> vcamPositions;
            [ReadOnly] public ComponentDataFromEntity<Position> positions;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                var p = vcamPositions[index];
                p = new CM_VcamPosition
                {
                    raw = p.raw,
                    dampingBypass = float3.zero,
                    up = math.up(),
                    previousFrameDataIsValid = p.previousFrameDataIsValid
                };

                var entity = entities[index];
                if (positions.Exists(entity))
                    p.raw = positions[entity].Value;

                vcamPositions[index] = p;
            }
        }

        [BurstCompile]
        struct InitRotJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamRotation> vcamRotations;
            [ReadOnly] public ComponentDataFromEntity<Rotation> rotations;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                var r = new CM_VcamRotation
                {
                    //lookAtPoint = CM_VcamRotation.kNoPoint, // GML fixme
                    raw = quaternion.identity,
                    correction = quaternion.identity
                };
                var entity = entities[index];
                if (rotations.Exists(entity))
                    r.raw = rotations[entity].Value;

                vcamRotations[index] = r;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var posJob = new InitPosJob
            {
                vcamPositions = m_posGroup.GetComponentDataArray<CM_VcamPosition>(),
                positions = GetComponentDataFromEntity<Position>(true),
                entities = m_posGroup.GetEntityArray()
            };
            var posDeps = posJob.Schedule(m_posGroup.CalculateLength(), 32, inputDeps);

            var rotJob = new InitRotJob
            {
                vcamRotations = m_rotGroup.GetComponentDataArray<CM_VcamRotation>(),
                rotations = GetComponentDataFromEntity<Rotation>(true),
                entities = m_rotGroup.GetEntityArray()
            };
            var rotDeps = rotJob.Schedule(m_rotGroup.CalculateLength(), 32, inputDeps);

            return JobHandle.CombineDependencies(posDeps, rotDeps);
        }
    }

    [UnityEngine.ExecuteInEditMode]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    public class CM_VcamPreAimSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [UnityEngine.ExecuteInEditMode]
    [UpdateAfter(typeof(CM_VcamPreAimSystem))]
    public class CM_VcamPreCorrectionSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [UnityEngine.ExecuteInEditMode]
    [UpdateAfter(typeof(CM_VcamPreCorrectionSystem))]
    public class CM_VcamFinalizeSystem : JobComponentSystem
    {
        ComponentGroup m_posGroup;
        ComponentGroup m_rotGroup;
        ComponentGroup m_transformGroup;

        protected override void OnCreateManager()
        {
            m_posGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamPosition>());
            m_rotGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamRotation>(),
                ComponentType.Create<Rotation>());
            m_transformGroup = GetComponentGroup(
                ComponentType.Create<LocalToWorld>(),
                ComponentType.ReadOnly<CM_VcamPosition>(),
                ComponentType.ReadOnly<CM_VcamRotation>());
        }

        [BurstCompile]
        struct FinalizePosJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamPosition> vcamPositions;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<Position> positions;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                var p = vcamPositions[index];
                vcamPositions[index] = new CM_VcamPosition
                {
                    raw = p.raw,
                    dampingBypass = float3.zero,
                    up = p.up,
                    previousFrameDataIsValid = 1
                };
                var entity = entities[index];
                if (positions.Exists(entity))
                    positions[entity] = new Position { Value = p.raw };
            }
        }

        [BurstCompile]
        struct FinalizeRotJob : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<CM_VcamRotation> vcamRotations;
            [NativeDisableParallelForRestriction] public ComponentDataArray<Rotation> rotations;

            public void Execute(int index)
            {
                rotations[index] = new Rotation { Value = vcamRotations[index].raw };
            }
        }

        [BurstCompile]
        struct PushToTransformJob : IJobParallelFor
        {
            public ComponentDataArray<LocalToWorld> localToWorld;
            [ReadOnly] public ComponentDataArray<CM_VcamPosition> vcamPositions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotation> vcamRotations;

            public void Execute(int index)
            {
                var m0 = localToWorld[index].Value;
                var m = new float3x3(m0.c0.xyz, m0.c1.xyz, m0.c2.xyz);
                var v = new float3(0.5773503f, 0.5773503f, 0.5773503f); // unit vector
                var scale = float4x4.Scale(math.length(math.mul(m, v))); // approximate uniform scale
                localToWorld[index] = new LocalToWorld
                {
                    Value = math.mul(new float4x4(vcamRotations[index].raw, vcamPositions[index].raw), scale)
                };
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var posJob = new FinalizePosJob
            {
                vcamPositions = m_posGroup.GetComponentDataArray<CM_VcamPosition>(),
                positions = GetComponentDataFromEntity<Position>(false),
                entities = m_posGroup.GetEntityArray()
            };
            var posDeps = posJob.Schedule(m_posGroup.CalculateLength(), 32, inputDeps);

            var rotJob = new FinalizeRotJob
            {
                vcamRotations = m_rotGroup.GetComponentDataArray<CM_VcamRotation>(),
                rotations = m_rotGroup.GetComponentDataArray<Rotation>(),
            };
            var rotDeps = rotJob.Schedule(m_rotGroup.CalculateLength(), 32, inputDeps);

            var transformJob = new PushToTransformJob
            {
                localToWorld = m_transformGroup.GetComponentDataArray<LocalToWorld>(),
                vcamPositions = m_transformGroup.GetComponentDataArray<CM_VcamPosition>(),
                vcamRotations = m_transformGroup.GetComponentDataArray<CM_VcamRotation>()
            };
            var transformDeps = transformJob.Schedule(m_transformGroup.CalculateLength(), 32, posDeps);

            return JobHandle.CombineDependencies(rotDeps, transformDeps);
        }
    }
}
