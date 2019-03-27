using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System;
using UnityEngine;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamHardLookAt : IComponentData
    {
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreAimSystem))]
    [UpdateBefore(typeof(CM_VcamPreCorrectionSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamHardLookAtSystem : JobComponentSystem
    {
        ComponentGroup m_vcamGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetComponentGroup(
                ComponentType.ReadWrite<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamHardLookAt>(),
                ComponentType.ReadOnly<CM_VcamLookAtTarget>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var targetSystem = World.GetExistingManager<CM_TargetSystem>();
            var targetLookup = targetSystem.GetTargetLookupForJobs(ref inputDeps);
            if (!targetLookup.IsCreated)
                return inputDeps; // no targets yet

            var job = new LookAtTargetJob() { targetLookup = targetLookup };
            var jobDeps = job.ScheduleGroup(m_vcamGroup, inputDeps);

            return targetSystem.RegisterTargetLookupReadJobs(jobDeps);
        }

        [BurstCompile]
        struct LookAtTargetJob : IJobProcessComponentData<
            CM_VcamRotationState, CM_VcamPositionState, CM_VcamLookAtTarget>
        {
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public void Execute(
                ref CM_VcamRotationState rotState,
                [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamLookAtTarget lookAt)
            {
                if (!targetLookup.TryGetValue(lookAt.target, out CM_TargetSystem.TargetInfo targetInfo))
                    return;

                var q = math.normalizesafe(rotState.raw, quaternion.identity);
                float3 dir = math.normalizesafe(targetInfo.position - posState.raw, math.forward(q));
                float3 up = math.normalizesafe(posState.up, math.up());
                q = q.LookRotationUnit(dir, up);
                rotState.lookAtPoint = targetInfo.position;
                rotState.lookAtRadius = targetInfo.radius;
                rotState.raw = q;
            }
        }
    }
}
