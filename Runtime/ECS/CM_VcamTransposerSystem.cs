using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System.Runtime.CompilerServices;
using UnityEngine;
using System;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamTransposer : IComponentData
    {
        /// <summary>
        /// The coordinate space to use when interpreting the offset from the target
        /// </summary>
        public enum BindingMode
        {
            /// <summary>
            /// Camera will be bound to the Follow target using a frame of reference consisting
            /// of the target's local frame, with the tilt and roll zeroed out.
            /// </summary>
            LockToTargetWithWorldUp,
            /// <summary>
            /// Camera will be bound to the Follow target using a frame of reference consisting
            /// of the target's local frame, with the roll zeroed out.
            /// </summary>
            LockToTargetNoRoll,
            /// <summary>
            /// Camera will be bound to the Follow target using the target's local frame.
            /// </summary>
            LockToTarget,
            /// <summary>Camera will be bound to the Follow target using a world space offset.</summary>
            WorldSpace,
            /// <summary>Offsets will be calculated relative to the target, using Camera-local axes</summary>
            SimpleFollowWithWorldUp
        }
        /// <summary>The coordinate space to use when interpreting the offset from the target</summary>
        public BindingMode bindingMode;

        /// <summary>The distance which the transposer will attempt to maintain from the transposer subject</summary>
        public float3 followOffset;

        /// <summary>How aggressively the camera tries to maintain the offset in the 3 axes.
        /// Small numbers are more responsive, rapidly translating the camera to keep the target's
        /// offset.  Larger numbers give a more heavy slowly responding camera.
        /// Using different settings per axis can yield a wide range of camera behaviors</summary>
        public float3 damping;

        /// <summary>How aggressively the camera tries to track the target's rotation.
        /// Small numbers are more responsive.  Larger numbers give a more heavy slowly responding camera.</summary>
        public float angularDamping;
    }

    [Serializable]
    public struct CM_VcamTransposerState : ISystemStateComponentData
    {
        /// State information used for damping
        public float3 previousTargetPosition;
        public quaternion previousTargetRotation;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    [UpdateBefore(typeof(CM_VcamPreAimSystem))]
    public class CM_VcamTransposerSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;
        ComponentGroup m_missingStateGroup;

#pragma warning disable 649 // never assigned to
        // Used only to add missing CM_VcamTransposerState components
        [Inject] EndFrameBarrier m_missingStateBarrier;
#pragma warning restore 649

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamPositionState>(),
                ComponentType.Create<CM_VcamTransposerState>(),
                ComponentType.ReadOnly<CM_VcamTransposer>(),
                ComponentType.ReadOnly<CM_VcamFollowTarget>());

            m_missingStateGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamPositionState>(),
                ComponentType.Subtractive<CM_VcamTransposerState>(),
                ComponentType.ReadOnly<CM_VcamTransposer>(),
                ComponentType.ReadOnly<CM_VcamFollowTarget>());
        }

        [BurstCompile]
        struct TrackTargetJob : IJobParallelFor
        {
            public float deltaTime;
            public float fixedDelta;
            public ComponentDataArray<CM_VcamPositionState> positions;
            public ComponentDataArray<CM_VcamTransposerState> transposerStates;
            [ReadOnly] public ComponentDataArray<CM_VcamTransposer> transposers;
            [ReadOnly] public ComponentDataArray<CM_VcamFollowTarget> targets;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public void Execute(int index)
            {
                CM_TargetSystem.TargetInfo targetInfo;
                if (targetLookup.TryGetValue(targets[index].target, out targetInfo))
                {
                    var targetPos = targetInfo.position;
                    var targetRot = GetRotationForBindingMode(
                            targetInfo.rotation, transposers[index].bindingMode,
                            targetPos - positions[index].raw);

                    bool applyDamping = deltaTime >= 0 && positions[index].previousFrameDataIsValid != 0;
                    targetRot = ApplyRotationDamping(
                        deltaTime, fixedDelta,
                        math.select(0, transposers[index].angularDamping, applyDamping),
                        transposerStates[index].previousTargetRotation, targetRot);
                    targetPos = ApplyPositionDamping(
                        deltaTime, fixedDelta,
                        math.select(float3.zero, transposers[index].damping, applyDamping),
                        transposerStates[index].previousTargetPosition, targetPos, targetRot);

                    transposerStates[index] = new CM_VcamTransposerState
                    {
                        previousTargetPosition = targetPos,
                        previousTargetRotation = targetRot
                    };

                    positions[index] = new CM_VcamPositionState
                    {
                        raw = targetPos + math.mul(targetRot, transposers[index].followOffset),
                        dampingBypass = float3.zero,
                        up = math.mul(targetRot, math.up())
                    };
                }
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing transposer state components
            var missingEntities = m_missingStateGroup.GetEntityArray();
            if (missingEntities.Length > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                for (int i = 0; i < missingEntities.Length; ++i)
                {
                    cb.AddComponent(missingEntities[i], new CM_VcamTransposerState());
                    cb.SetComponent(missingEntities[i], new CM_VcamPositionState()); // invalidate prev pos
                }
            }

            var targetSystem = World.GetExistingManager<CM_TargetSystem>();
            var targetLookup = targetSystem.GetTargetLookupForJobs(ref inputDeps);
            if (!targetLookup.IsCreated)
                return default; // no targets yet

            var job = new TrackTargetJob()
            {
                deltaTime = Time.deltaTime, // GML todo: use correct value
                fixedDelta = Time.fixedDeltaTime,
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPositionState>(),
                transposers = m_mainGroup.GetComponentDataArray<CM_VcamTransposer>(),
                transposerStates = m_mainGroup.GetComponentDataArray<CM_VcamTransposerState>(),
                targets = m_mainGroup.GetComponentDataArray<CM_VcamFollowTarget>(),
                targetLookup =  targetLookup
            };
            return targetSystem.RegisterTargetLookupReadJobs(
                job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps));
        }

        /// <summary>Applies damping to target rotation</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion ApplyRotationDamping(
            float deltaTime, float fixedDelta, float angularDamping,
            quaternion previousTargetRotation, quaternion currentTargetRotation)
        {
            float t = MathHelpers.Damp(1, angularDamping, deltaTime, fixedDelta);
            return math.slerp(previousTargetRotation, currentTargetRotation, t);
        }

        /// <summary>Applies damping to target position</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 ApplyPositionDamping(
            float deltaTime, float fixedDelta, float3 damping,
            float3 previousTargetPosition, float3 currentTargetPosition,
            quaternion currentTargetRotation)
        {
            var worldOffset = currentTargetPosition - previousTargetPosition;
            float3 localOffset = math.mul(math.inverse(currentTargetRotation), worldOffset);
            localOffset = MathHelpers.Damp(localOffset, damping, deltaTime, fixedDelta);
            worldOffset = math.mul(currentTargetRotation, localOffset);
            return previousTargetPosition + worldOffset;
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
                    directionCameraToTarget.y = 0;
                    float len = math.length(directionCameraToTarget);
                    return math.select(
                        quaternion.LookRotation(directionCameraToTarget / len, math.up()).value,
                        targetRotation.value, len < MathHelpers.Epsilon);
                }
                default:
                    return quaternion.identity;
            }
        }
    }
}
