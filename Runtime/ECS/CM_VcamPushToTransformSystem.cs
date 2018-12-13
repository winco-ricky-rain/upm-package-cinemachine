#if false

using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Transforms;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    public class CM_VcamPushToTransformSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.Create<LocalToWorld>(),
                ComponentType.ReadOnly<CM_VcamPosition>(),
                ComponentType.ReadOnly<CM_VcamRotation>());
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
            var job = new PushToTransformJob
            {
                localToWorld = m_mainGroup.GetComponentDataArray<LocalToWorld>(),
                vcamPositions = m_mainGroup.GetComponentDataArray<CM_VcamPosition>(),
                vcamRotations = m_mainGroup.GetComponentDataArray<CM_VcamRotation>()
            };
            return job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps);
        }
    }
}
#endif

