using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine.Jobs;
using Cinemachine.ECS;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.ExecuteInEditMode]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    public class PushToVcamTransformSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                typeof(UnityEngine.Transform),
                ComponentType.ReadOnly(typeof(CM_VcamPosition)),
                ComponentType.ReadOnly(typeof(CM_VcamRotation)));
        }

        [BurstCompile]
        struct CopyTransforms : IJobParallelForTransform
        {
            [ReadOnly] public ComponentDataArray<CM_VcamPosition> positions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotation> rotations;

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
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPosition>(),
                rotations = m_mainGroup.GetComponentDataArray<CM_VcamRotation>()
            };
            return job.Schedule(transforms, inputDeps);
        }
    }
}
