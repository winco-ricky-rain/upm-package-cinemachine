using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Cinemachine.ECS
{
    // These systems define the CM Vcam pipeline, in this order.
    // Use them to ensure correct ordering of CM pipeline systems

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_TargetSystem))]
    public class CM_VcamPreBodySystem : JobComponentSystem
    {
        ComponentGroup m_posGroup;
        ComponentGroup m_rotGroup;
        ComponentGroup m_lensGroup;
        ComponentGroup m_missingPosStateGroup;
        ComponentGroup m_missingRotStateGroup;
        ComponentGroup m_missingLensStateGroup;

#pragma warning disable 649 // never assigned to
        // Used only to add missing state components
        [Inject] EndFrameBarrier m_missingStateBarrier;
#pragma warning restore 649

        protected override void OnCreateManager()
        {
            m_posGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamPositionState>());
            m_rotGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamRotationState>());
            m_lensGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamLens>(),
                ComponentType.Create<CM_VcamLensState>());

            m_missingPosStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamPriority>(),
                ComponentType.Subtractive<CM_VcamPositionState>());
            m_missingRotStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamPriority>(),
                ComponentType.Subtractive<CM_VcamRotationState>());
            m_missingLensStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamPriority>(),
                ComponentType.Subtractive<CM_VcamLensState>());
        }

        [BurstCompile]
        struct InitPosJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamPositionState> vcamPositions;
            [ReadOnly] public ComponentDataFromEntity<Position> positions;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                var p = vcamPositions[index];
                p.dampingBypass = float3.zero;
                p.up = math.up(); // GML todo: fixme

                var entity = entities[index];
                if (positions.Exists(entity))
                    p.raw = positions[entity].Value;

                vcamPositions[index] = p;
            }
        }

        [BurstCompile]
        struct InitRotJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamRotationState> vcamRotations;
            [ReadOnly] public ComponentDataFromEntity<Rotation> rotations;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                var r = vcamRotations[index];
                r.correction = quaternion.identity;
                vcamRotations[index] = r;

                var entity = entities[index];
                if (rotations.Exists(entity))
                    r.raw = rotations[entity].Value;

                vcamRotations[index] = r;
            }
        }

        [BurstCompile]
        struct InitLensJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamLensState> lensStates;
            [ReadOnly] public ComponentDataArray<CM_VcamLens> lenses;

            public void Execute(int index)
            {
                lensStates[index] = CM_VcamLensState.FromLens(lenses[index]);
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing state components
            var missingPosEntities = m_missingPosStateGroup.GetEntityArray();
            var missingRotEntities = m_missingRotStateGroup.GetEntityArray();
            var missingLensEntities = m_missingLensStateGroup.GetEntityArray();
            if (missingPosEntities.Length + missingRotEntities.Length + missingLensEntities.Length > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                for (int i = 0; i < missingPosEntities.Length; ++i)
                    cb.AddComponent(missingPosEntities[i], new CM_VcamPositionState());
                for (int i = 0; i < missingRotEntities.Length; ++i)
                    cb.AddComponent(missingRotEntities[i], new CM_VcamRotationState
                        { raw = quaternion.identity, correction = quaternion.identity });
                for (int i = 0; i < missingRotEntities.Length; ++i)
                    cb.AddComponent(missingRotEntities[i], CM_VcamLensState.FromLens(CM_VcamLens.Default));
            }

            var posJob = new InitPosJob
            {
                vcamPositions = m_posGroup.GetComponentDataArray<CM_VcamPositionState>(),
                positions = GetComponentDataFromEntity<Position>(true),
                entities = m_posGroup.GetEntityArray()
            };
            var posDeps = posJob.Schedule(m_posGroup.CalculateLength(), 32, inputDeps);

            var rotJob = new InitRotJob
            {
                vcamRotations = m_rotGroup.GetComponentDataArray<CM_VcamRotationState>(),
                rotations = GetComponentDataFromEntity<Rotation>(true),
                entities = m_rotGroup.GetEntityArray()
            };
            var rotDeps = rotJob.Schedule(m_rotGroup.CalculateLength(), 32, inputDeps);

            var lensJob = new InitLensJob
            {
                lensStates = m_lensGroup.GetComponentDataArray<CM_VcamLensState>(),
                lenses = m_lensGroup.GetComponentDataArray<CM_VcamLens>()
            };
            var lensDeps = lensJob.Schedule(m_lensGroup.CalculateLength(), 32, inputDeps);

            return JobHandle.CombineDependencies(posDeps, rotDeps, lensDeps);
        }
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    public class CM_VcamPreAimSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreAimSystem))]
    public class CM_VcamPreCorrectionSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreCorrectionSystem))]
    public class CM_VcamFinalizeSystem : JobComponentSystem
    {
        ComponentGroup m_posGroup;
        ComponentGroup m_rotGroup;
        ComponentGroup m_transformGroup;

        protected override void OnCreateManager()
        {
            m_posGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamPositionState>());
            m_rotGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamRotationState>(),
                ComponentType.Create<Rotation>());
            m_transformGroup = GetComponentGroup(
                ComponentType.Create<LocalToWorld>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>());
        }

        [BurstCompile]
        struct FinalizePosJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamPositionState> vcamPositions;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<Position> positions;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                var p = vcamPositions[index];
                vcamPositions[index] = new CM_VcamPositionState
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
            [ReadOnly] public ComponentDataArray<CM_VcamRotationState> vcamRotations;
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
            [ReadOnly] public ComponentDataArray<CM_VcamPositionState> vcamPositions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotationState> vcamRotations;

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
                vcamPositions = m_posGroup.GetComponentDataArray<CM_VcamPositionState>(),
                positions = GetComponentDataFromEntity<Position>(false),
                entities = m_posGroup.GetEntityArray()
            };
            var posDeps = posJob.Schedule(m_posGroup.CalculateLength(), 32, inputDeps);

            var rotJob = new FinalizeRotJob
            {
                vcamRotations = m_rotGroup.GetComponentDataArray<CM_VcamRotationState>(),
                rotations = m_rotGroup.GetComponentDataArray<Rotation>(),
            };
            var rotDeps = rotJob.Schedule(m_rotGroup.CalculateLength(), 32, inputDeps);

            var transformJob = new PushToTransformJob
            {
                localToWorld = m_transformGroup.GetComponentDataArray<LocalToWorld>(),
                vcamPositions = m_transformGroup.GetComponentDataArray<CM_VcamPositionState>(),
                vcamRotations = m_transformGroup.GetComponentDataArray<CM_VcamRotationState>()
            };
            var transformDeps = transformJob.Schedule(m_transformGroup.CalculateLength(), 32, posDeps);

            return JobHandle.CombineDependencies(rotDeps, transformDeps);
        }
    }
}
