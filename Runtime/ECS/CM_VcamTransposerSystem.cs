using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Cinemachine.ECS
{
    public class CM_VcamTransposerSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                typeof(CM_VcamPosition), 
                typeof(CM_VcamTransposerState), 
                ComponentType.ReadOnly(typeof(CM_VcamTransposer)),
                ComponentType.ReadOnly(typeof(CM_VcamFollowTarget)));
       }

        [BurstCompile]
        struct TrackTargetJob : IJobParallelFor
        {
            public float deltaTime;
            public ComponentDataArray<CM_VcamPosition> positions;
            public ComponentDataArray<CM_VcamTransposerState> transposerStates;
            [ReadOnly] public ComponentDataArray<CM_VcamTransposer> transposers;
            [ReadOnly] public ComponentDataArray<CM_VcamFollowTarget> targets;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public void Execute(int index)
            {
                CM_TargetSystem.TargetInfo targetInfo;
                if (targetLookup.TryGetValue(targets[index].target, out targetInfo))
                {
                    // Track it!
                    var bindingMode = transposers[index].bindingMode;
                    var targetPos = targetInfo.position;
                    var targetRot = GetRotationForBindingMode(
                        targetInfo.rotation, bindingMode, 
                        targetPos - positions[index].raw);
                    var previousPos = math.select(transposerStates[index].previousTargetPosition, targetPos, deltaTime >= 0);
                    var previousRot = math.select(transposerStates[index].previousTargetRotation.value, targetRot.value, deltaTime >= 0);

                    // No X damping if simplefollow
                    var damping = transposers[index].damping;
                    damping.x = math.select(
                        damping.y, 0, bindingMode == CM_VcamTransposer.BindingMode.SimpleFollowWithWorldUp);

                    ApplyDamping(
                        deltaTime, damping, transposers[index].angularDamping, 
                        previousPos, previousRot, ref targetPos, ref targetRot);
                    transposerStates[index] = new CM_VcamTransposerState 
                    { 
                        previousTargetPosition = targetPos, 
                        previousTargetRotation = targetRot
                    };
                    positions[index] = new CM_VcamPosition
                    {
                        raw = targetPos + math.mul(targetRot, transposers[index].followOffset),
                        dampingBypass = positions[index].dampingBypass,
                        up = math.mul(targetRot, math.up())
                    };
                }
            }
        }
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new TrackTargetJob()
            {
                deltaTime = Time.deltaTime, // GML todo: use correct value
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPosition>(),
                transposers = m_mainGroup.GetComponentDataArray<CM_VcamTransposer>(),
                targets = m_mainGroup.GetComponentDataArray<CM_VcamFollowTarget>(),
                targetLookup = World.GetExistingManager<CM_TargetSystem>().TargetLookup
            };
            return job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps);
        }

        /// <summary>Applies damping to target position and rtotation</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ApplyDamping(
            float deltaTime, float3 damping, float angularDamping,
            float3 previousTargetPosition, quaternion previousTargetRotation,
            ref float3 currentTargetPosition, ref quaternion currentTargetRotation)
        {
            float t = MathHelpers.Damp(1, angularDamping, deltaTime);
            currentTargetRotation = math.slerp(previousTargetRotation, currentTargetRotation, t);

            var worldOffset = currentTargetPosition - previousTargetPosition;
            float3 localOffset = math.mul(math.inverse(currentTargetRotation), worldOffset);
            localOffset = MathHelpers.Damp(localOffset, damping, deltaTime);
            worldOffset = math.mul(currentTargetRotation, localOffset);
            currentTargetPosition = previousTargetPosition + worldOffset;
        }

        /// <summary>Applies binding mode: 
        /// Returns the axes for applying target offset and damping</summary>
        public static quaternion GetRotationForBindingMode(
            quaternion targetRotation, 
            CM_VcamTransposer.BindingMode bindingMode, 
            float3 directionCameraToTarget) // not normalized
        {
            // GML todo: optimize!  Can we get rid of the switch?
            switch (bindingMode)
            {
                case CM_VcamTransposer.BindingMode.LockToTargetWithWorldUp:
                    return MathHelpers.Uppify(targetRotation, math.up());
                case CM_VcamTransposer.BindingMode.LockToTargetNoRoll:
                    return quaternion.LookRotationSafe(math.forward(targetRotation), math.up());
                case CM_VcamTransposer.BindingMode.LockToTarget:
                    return targetRotation;
                case CM_VcamTransposer.BindingMode.SimpleFollowWithWorldUp:
                {
                    float3 dir = directionCameraToTarget.ProjectOntoPlane(math.up());
                    float len = math.length(dir);
                    return new quaternion(math.select(
                        quaternion.LookRotation(dir / len, math.up()).value, 
                        quaternion.identity.value, len < MathHelpers.Epsilon));
                }
                default:
                    return quaternion.identity;
            }
        }
    }
}
