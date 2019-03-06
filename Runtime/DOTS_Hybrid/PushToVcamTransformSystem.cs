using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Jobs;
using Cinemachine.ECS;
using UnityEngine;
using Unity.Transforms;
using Unity.Mathematics;

namespace Cinemachine.ECS_Hybrid
{
    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class PushToVcamTransformSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                typeof(Transform),
                ComponentType.ReadOnly(typeof(CM_VcamPositionState)),
                ComponentType.ReadOnly(typeof(CM_VcamRotationState)));
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var transforms = m_mainGroup.GetTransformAccessArray();
            var transformStashes = new NativeArray<TransformStash>(transforms.length, Allocator.TempJob);
            var stashJob = new StashTransforms { transformStashes = transformStashes };
            var stashDeps = stashJob.ScheduleGroup(m_mainGroup, inputDeps);

            var writeJob = new WriteTransforms { transformStashes = transformStashes };
            return writeJob.Schedule(transforms, stashDeps);
        }

        struct TransformStash
        {
            public float3 position;
            public quaternion rotation;
        }

        [BurstCompile]
        struct StashTransforms : IJobProcessComponentDataWithEntity<CM_VcamPositionState, CM_VcamRotationState>
        {
            public NativeArray<TransformStash> transformStashes;

            public void Execute(
                Entity entity, int index,
                [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState)
            {
                transformStashes[index] = new TransformStash
                {
                    rotation = rotState.raw,
                    position = posState.raw,
                };
            }
        }

        [BurstCompile]
        struct WriteTransforms : IJobParallelForTransform
        {
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<TransformStash> transformStashes;
            public void Execute(int index, TransformAccess transform)
            {
                var stash = transformStashes[index];
                transform.position = stash.position;
                transform.rotation = stash.rotation;
            }
        }
    }
}
