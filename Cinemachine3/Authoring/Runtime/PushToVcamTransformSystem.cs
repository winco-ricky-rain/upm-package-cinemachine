using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Jobs;
using Unity.Cinemachine3;
using UnityEngine;
using Unity.Mathematics;

namespace Unity.Cinemachine3.Authoring
{
    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class PushToVcamTransformSystem : JobComponentSystem
    {
        EntityQuery m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetEntityQuery(
                typeof(Transform),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>(),
                ComponentType.Exclude<CM_Channel>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var transforms = m_mainGroup.GetTransformAccessArray();
            var transformStashes = new NativeArray<TransformStash>(transforms.length, Allocator.TempJob);
            var stashJob = new StashTransforms { transformStashes = transformStashes };
            var stashDeps = stashJob.Schedule(m_mainGroup, inputDeps);

            var writeJob = new WriteTransforms { transformStashes = transformStashes };
            return writeJob.Schedule(transforms, stashDeps);
        }

        struct TransformStash
        {
            public float3 position;
            public quaternion rotation;
        }

        [BurstCompile]
        struct StashTransforms : IJobForEachWithEntity<CM_VcamPositionState, CM_VcamRotationState>
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
