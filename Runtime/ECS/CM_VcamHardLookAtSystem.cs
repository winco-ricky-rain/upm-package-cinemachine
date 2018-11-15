using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

namespace Cinemachine.ECS
{
    public class CM_VcamHardLookAtSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                typeof(CM_VcamRotation), 
                ComponentType.ReadOnly(typeof(CM_VcamPosition)), 
                ComponentType.ReadOnly(typeof(CM_VcamLookAtTarget)));
        }

        [BurstCompile]
        struct LookAtTargetJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamRotation> rotations;
            [ReadOnly] public ComponentDataArray<CM_VcamPosition> positions;
            [ReadOnly] public ComponentDataArray<CM_VcamLookAtTarget> targets;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public void Execute(int index)
            {
                CM_TargetSystem.TargetInfo targetInfo;
                if (targetLookup.TryGetValue(targets[index].target, out targetInfo))
                {
                    var q = rotations[index].raw;
                    float3 dir = math.normalizesafe(
                        targetInfo.position - positions[index].raw, math.forward(q));
                    q = q.LookRotationUnit(dir, positions[index].up);
                    rotations[index] = new CM_VcamRotation
                    {
                        lookAtPoint = targetInfo.position,
                        raw = q
                    };
                }
            }
        }
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new LookAtTargetJob()
            {
                rotations = m_mainGroup.GetComponentDataArray<CM_VcamRotation>(),
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPosition>(),
                targets = m_mainGroup.GetComponentDataArray<CM_VcamLookAtTarget>(),
                targetLookup = World.GetExistingManager<CM_TargetSystem>().TargetLookup
            };
            return job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps);
        }
    }
}
