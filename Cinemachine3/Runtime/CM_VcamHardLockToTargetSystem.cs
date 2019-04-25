using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System;
using UnityEngine;

namespace Unity.Cinemachine3
{
    [Serializable]
    public struct CM_VcamHardLockToTarget : IComponentData
    {
        public bool lockRotation;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    [UpdateBefore(typeof(CM_VcamPreAimSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamHardLockToTargetSystem : JobComponentSystem
    {
        EntityQuery m_vcamGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetEntityQuery(
                ComponentType.ReadWrite<CM_VcamPositionState>(),
                ComponentType.ReadWrite<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamFollowTarget>(),
                ComponentType.ReadOnly<CM_VcamHardLockToTarget>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var targetSystem = World.GetExistingSystem<CM_TargetSystem>();
            var targetLookup = targetSystem.GetTargetLookupForJobs(ref inputDeps);
            if (!targetLookup.IsCreated)
                return inputDeps; // no targets yet

            var job = new LockToTargetJob() { targetLookup = targetLookup };
            var jobDeps = job.Schedule(m_vcamGroup, inputDeps);

            return targetSystem.RegisterTargetLookupReadJobs(jobDeps);
        }

        [BurstCompile]
        struct LockToTargetJob : IJobForEach<
            CM_VcamPositionState, CM_VcamRotationState,
            CM_VcamHardLockToTarget, CM_VcamFollowTarget>
        {
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public void Execute(
                ref CM_VcamPositionState posState,
                ref CM_VcamRotationState rotState,
                [ReadOnly] ref CM_VcamHardLockToTarget hardLock,
                [ReadOnly] ref CM_VcamFollowTarget follow)
            {
                if (!targetLookup.TryGetValue(follow.target, out CM_TargetSystem.TargetInfo targetInfo))
                    return;
                posState.raw = targetInfo.position;
                rotState.raw = math.select(rotState.raw.value, targetInfo.rotation.value, hardLock.lockRotation);
            }
        }
    }
}
