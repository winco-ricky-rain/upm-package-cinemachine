using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System.Runtime.CompilerServices;
using UnityEngine;
using System;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3
{
    [Serializable]
    public struct CM_VcamTransposer : IComponentData
    {
        /// <summary>The coordinate space to use when interpreting the offset from the target</summary>
        public CM_VcamTransposerSystem.BindingMode bindingMode;

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
    public struct CM_VcamTransposerState : IComponentData
    {
        /// State information used for damping
        public float3 previousTargetPosition;
        public quaternion previousTargetRotation;
        public float3 previousTargetOffset;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    [UpdateBefore(typeof(CM_VcamPreAimSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamTransposerSystem : JobComponentSystem
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

        EntityQuery m_vcamGroup;
        EntityQuery m_missingStateGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetEntityQuery(
                ComponentType.ReadWrite<CM_VcamPositionState>(),
                ComponentType.ReadWrite<CM_VcamTransposerState>(),
                ComponentType.ReadOnly<CM_VcamTransposer>(),
                ComponentType.ReadOnly<CM_VcamFollowTarget>(),
                ComponentType.ReadOnly<CM_VcamChannel>());

            m_missingStateGroup = GetEntityQuery(
                ComponentType.Exclude<CM_VcamTransposerState>(),
                ComponentType.ReadOnly<CM_VcamTransposer>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing transposer state components
            EntityManager.AddComponent(m_missingStateGroup,
                ComponentType.ReadWrite<CM_VcamTransposerState>());

            var targetSystem = World.GetOrCreateSystem<CM_TargetSystem>();
            var targetLookup = targetSystem.GetTargetLookupForJobs(ref inputDeps);
            if (!targetLookup.IsCreated)
                return inputDeps; // no targets yet

            var channelSystem = World.GetOrCreateSystem<CM_ChannelSystem>();
            JobHandle vcamDeps = channelSystem.ScheduleForVcamsOnAllChannels(
                m_vcamGroup, inputDeps,
                new TrackTargetJob { targetLookup = targetLookup });

            return targetSystem.RegisterTargetLookupReadJobs(vcamDeps);
        }

        [BurstCompile]
        struct TrackTargetJob : CM_ChannelSystem.IVcamPerChannelJob, IJobForEach<
            CM_VcamPositionState, CM_VcamTransposerState,
            CM_VcamTransposer, CM_VcamFollowTarget>
        {
            public float deltaTime;
            public float3 up;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public bool InitializeForChannel(
                Entity channelEntity, CM_Channel c, CM_ChannelState state)
            {
                deltaTime = state.deltaTime;
                up = math.mul(c.settings.worldOrientation, math.up());
                return true;
            }

            public void Execute(
                ref CM_VcamPositionState posState,
                ref CM_VcamTransposerState transposerState,
                [ReadOnly] ref CM_VcamTransposer transposer,
                [ReadOnly] ref CM_VcamFollowTarget follow)
            {
                if (!targetLookup.TryGetValue(follow.target, out CM_TargetSystem.TargetInfo targetInfo))
                    return;

                float dt = math.select(-1, deltaTime, posState.previousFrameDataIsValid);

                var targetPos = targetInfo.position;
                var targetRot = GetRotationForBindingMode(
                        targetInfo.rotation, transposer.bindingMode,
                        targetPos - posState.raw, up);

                var prevPos = transposerState.previousTargetPosition;
                targetRot = ApplyRotationDamping(
                    dt, 0,
                    math.select(0, transposer.angularDamping, dt >= 0),
                    transposerState.previousTargetRotation, targetRot);
                targetPos = ApplyPositionDamping(
                    dt, 0,
                    math.select(float3.zero, transposer.damping, dt >= 0),
                    prevPos, targetPos, targetRot);

                var followOffset = math.mul(targetRot, transposer.followOffset);
                followOffset.x = math.select(
                    followOffset.x, 0, transposer.bindingMode == BindingMode.SimpleFollowWithWorldUp);
                posState.raw = targetPos + followOffset;
                posState.up = math.mul(targetRot, math.up());
                posState.dampingBypass = followOffset - transposerState.previousTargetOffset;

                transposerState.previousTargetPosition = targetPos;
                transposerState.previousTargetRotation = targetRot;
                transposerState.previousTargetOffset = followOffset;
            }
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
            BindingMode bindingMode,
            float3 directionCameraToTarget, // not normalized
            float3 up)
        {
            // GML todo: optimize!  Can we get rid of the switch?
            switch (bindingMode)
            {
                case BindingMode.LockToTargetWithWorldUp:
                    return MathHelpers.Uppify(targetRotation, up);
                case BindingMode.LockToTargetNoRoll:
                    return quaternion.LookRotationSafe(math.forward(targetRotation), up);
                case BindingMode.LockToTarget:
                    return targetRotation;
                case BindingMode.SimpleFollowWithWorldUp:
                {
                    directionCameraToTarget = directionCameraToTarget.ProjectOntoPlane(up);
                    float len = math.length(directionCameraToTarget);
                    return math.select(
                        quaternion.LookRotation(directionCameraToTarget / len, up).value,
                        targetRotation.value, len < MathHelpers.Epsilon);
                }
                default:
                    return quaternion.identity;
            }
        }
    }
}
