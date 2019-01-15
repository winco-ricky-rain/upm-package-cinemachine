using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Jobs;
using Cinemachine.ECS;
using UnityEngine;

namespace Cinemachine.ECS_Hybrid
{
    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
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

        [BurstCompile]
        struct CopyTransforms : IJobParallelForTransform
        {
            [ReadOnly] public ComponentDataArray<CM_VcamPositionState> positions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotationState> rotations;

            public void Execute(int index, TransformAccess transform)
            {
                transform.position = positions[index].raw;
                transform.rotation = rotations[index].raw;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var transforms = m_mainGroup.GetTransformAccessArray();
            var job = new CopyTransforms
            {
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPositionState>(),
                rotations = m_mainGroup.GetComponentDataArray<CM_VcamRotationState>()
            };
            return job.Schedule(transforms, inputDeps);
        }
    }
}
