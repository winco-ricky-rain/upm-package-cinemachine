using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System;
using UnityEngine;
using System.Runtime.CompilerServices;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3
{
    [Serializable]
    public struct CM_VcamSizeFraming : IComponentData
    {
        public const float kMinScreenFitSize = 0.01f;

        /// <summary>How much of the screen to fill with the bounding box of the targets.</summary>
        [Tooltip("The bounding box of the targets should occupy this amount of the screen space.  "
            + "1 means fill the whole screen.  0.5 means fill half the screen, etc.")]
        [CM_Float2AsRangeProperty]
        public float2 screenFit;

        /// <summary>What screen dimensions to consider when framing</summary>
        public enum FramingMode
        {
            /// <summary>Consider only the horizontal dimension.  Vertical framing is ignored.</summary>
            Horizontal,
            /// <summary>Consider only the vertical dimension.  Horizontal framing is ignored.</summary>
            Vertical,
            /// <summary>The larger of the horizontal and vertical dimensions will dominate, to get the best fit.</summary>
            HorizontalAndVertical
        };

        /// <summary>What screen dimensions to consider when framing</summary>
        [Tooltip("What screen dimensions to consider when framing.  "
            + "Can be Horizontal, Vertical, or both")]
        public FramingMode framingMode;

        /// <summary>How aggressively the camera tries to frame the group.
        /// Small numbers are more responsive</summary>
        [Tooltip("How aggressively the camera tries to frame the target. Small numbers are "
            + "more responsive, rapidly adjusting the camera to keep the group in the frame.  "
            + "Larger numbers give a more heavy slowly responding camera.")]
        public float damping;

        /// <summary>How to adjust the camera to get the desired framing</summary>
        public enum AdjustmentMode
        {
            /// <summary>Do not move the camera, only adjust the FOV.</summary>
            ZoomOnly,
            /// <summary>Just move the camera, don't change the FOV.</summary>
            DollyOnly,
            /// <summary>Move the camera as much as permitted by the ranges, then
            /// adjust the FOV if necessary to make the shot.</summary>
            DollyThenZoom
        };

        /// <summary>How to adjust the camera to get the desired framing</summary>
        [Tooltip("How to adjust the camera to get the desired framing.  "
            + "You can zoom, dolly in/out, or do both.")]
        public AdjustmentMode adjustmentMode;

        /// <summary>The maximum distance that this behaviour is allowed to move the camera>
        [Tooltip("The maximum distance that this behaviour is allowed to move the camera.")]
        [CM_Float2AsRangeProperty]
        public float2 dollyRange;

        /// <summary>Set this to limit how close to the target the camera can get</summary>
        [Tooltip("Set this to limit how close to the target the camera can get.")]
        [CM_Float2AsRangeProperty]
        public float2 targetDistance;

        /// <summary>If adjusting FOV, will not set the FOV outside of this range</summary>
        [Tooltip("If adjusting FOV, will not set the FOV outside of this range.")]
        [CM_Float2AsRangeProperty]
        public float2 fovRange;

        /// <summary>If adjusting Orthographic Size, will not set it outside of this range</summary>
        [Tooltip("If adjusting Orthographic Size, will not set it outside of this range.")]
        [CM_Float2AsRangeProperty]
        public float2 orthoSizeRange;
    }

    // Internal use only
    struct CM_VcamSizeFramingState : IComponentData
    {
        public float prevFramingDistance;
        public float prevFOV;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamComposerSystem))]
    [UpdateBefore(typeof(CM_VcamPreCorrectionSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamSizeFramingSystem : JobComponentSystem
    {
        EntityQuery m_vcamGroup;
        EntityQuery m_missingStateGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetEntityQuery(
                ComponentType.ReadWrite<CM_VcamSizeFramingState>(),
                ComponentType.ReadWrite<CM_VcamPositionState>(),
                ComponentType.ReadWrite<CM_VcamLensState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamLookAtTarget>(),
                ComponentType.ReadOnly<CM_VcamSizeFraming>(),
                ComponentType.ReadOnly<CM_VcamChannel>());

            m_missingStateGroup = GetEntityQuery(
                ComponentType.ReadOnly<CM_VcamSizeFraming>(),
                ComponentType.Exclude<CM_VcamSizeFramingState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing composer state components
            EntityManager.AddComponent(m_missingStateGroup,
                ComponentType.ReadWrite<CM_VcamSizeFramingState>());

            var targetSystem = World.GetOrCreateSystem<CM_TargetSystem>();
            var targetLookup = targetSystem.GetTargetLookupForJobs(ref inputDeps);
            if (!targetLookup.IsCreated)
                return inputDeps; // no targets yet

            var channelSystem = World.GetOrCreateSystem<CM_ChannelSystem>();
            JobHandle framingDeps = channelSystem.InvokePerVcamChannel(
                m_vcamGroup, inputDeps, new InitVcamJobLaunch { targetLookup = targetLookup });

            return targetSystem.RegisterTargetLookupReadJobs(framingDeps);
        }

        struct InitVcamJobLaunch : CM_ChannelSystem.IPerChannelJobLauncher
        {
            public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;
            public JobHandle Execute(
                EntityQuery vcams, Entity channelEntity,
                CM_Channel c, CM_ChannelState state, JobHandle inputDeps)
            {
                if (c.settings.IsOrthographic)
                {
                    var orthoJob = new SizeFramingJobOrtho
                    {
                        deltaTime = state.deltaTime,
                        fixedDelta = 0, //GML Time.fixedDeltaTime,
                        aspect = c.settings.aspect,
                        targetLookup = targetLookup
                    };
                    return orthoJob.Schedule(vcams, inputDeps);
                }
                var job = new SizeFramingJob
                {
                    deltaTime = state.deltaTime,
                    fixedDelta = 0, //GML Time.fixedDeltaTime,
                    aspect = c.settings.aspect,
                    targetLookup = targetLookup
                };
                return job.Schedule(vcams, inputDeps);
            }
        }

        // Helper for jobs
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float GetTargetHeight(float radius, CM_VcamSizeFraming.FramingMode mode, float aspect)
        {
            float v = math.max(MathHelpers.Epsilon, radius * 2);
            float h = v / aspect;
            return math.select(
                math.select(h, v, mode == CM_VcamSizeFraming.FramingMode.Vertical),
                math.max(h, v),
                mode == CM_VcamSizeFraming.FramingMode.HorizontalAndVertical);
        }

        [BurstCompile]
        struct SizeFramingJob : IJobForEach<
            CM_VcamSizeFramingState, CM_VcamLensState, CM_VcamPositionState,
            CM_VcamRotationState, CM_VcamLookAtTarget, CM_VcamSizeFraming>
        {
            public float deltaTime;
            public float fixedDelta;
            public float aspect;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public void Execute(
                ref CM_VcamSizeFramingState framingState,
                ref CM_VcamLensState lensState,
                ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState,
                [ReadOnly] ref CM_VcamLookAtTarget lookAt,
                [ReadOnly] ref CM_VcamSizeFraming framing)
            {
                if (!targetLookup.TryGetValue(lookAt.target, out CM_TargetSystem.TargetInfo targetInfo))
                    return;

                var cameraPos = posState.GetCorrected();
                var fwd = targetInfo.position - cameraPos;
                float d = math.length(fwd);
                if (d < MathHelpers.Epsilon || targetInfo.radius < CM_VcamSizeFraming.kMinScreenFitSize)
                    return;

                fwd /= d;
                float h = GetTargetHeight(targetInfo.radius, framing.framingMode, aspect);
                float2 height2 = h / math.max(CM_VcamSizeFraming.kMinScreenFitSize, framing.screenFit);
                float dt = math.select(-1, deltaTime, posState.previousFrameDataIsValid);

                // Move the camera
                if (framing.adjustmentMode != CM_VcamSizeFraming.AdjustmentMode.ZoomOnly)
                {
                    // What distance would be needed to get the target height, at the current FOV
                    float2 targetDistance2 = height2 / (2f * math.tan(math.radians(lensState.fov) * 0.5f));
                    float targetDistance = math.select(
                        targetDistance2.y,
                        math.select(targetDistance2.x, d, d < targetDistance2.x),
                        d > targetDistance2.y);

                    // Clamp to respect min/max distance settings
                    targetDistance = math.clamp(
                        targetDistance, framing.targetDistance.x, framing.targetDistance.y);

                    // Clamp to respect min/max camera movement
                    float targetDelta = math.clamp(
                        targetDistance - d, framing.dollyRange.x, framing.dollyRange.y);

                    // Apply damping
                    targetDelta = framingState.prevFramingDistance + MathHelpers.Damp(
                        targetDelta - framingState.prevFramingDistance,
                        math.select(0, framing.damping, dt >= 0), dt);

                    framingState.prevFramingDistance = targetDelta;
                    posState.correction -= fwd * targetDelta;
                    d += targetDelta;
                }

                // Apply zoom
                if (framing.adjustmentMode != CM_VcamSizeFraming.AdjustmentMode.DollyOnly)
                {
                    float current = lensState.fov;
                    float2 targetFOV2 = 2f * math.degrees(math.atan(height2 / (2 * d)));
                    float targetFOV = math.select(
                        targetFOV2.y,
                        math.select(targetFOV2.x, current, current < targetFOV2.x),
                        current > targetFOV2.y);

                    // Apply damping
                    targetFOV = math.select(
                        targetFOV, framingState.prevFOV + MathHelpers.Damp(
                            targetFOV - framingState.prevFOV, framing.damping, dt),
                        dt >= 0);
                    lensState.fov = targetFOV;
                }
                framingState.prevFOV = lensState.fov;
            }
        }

        [BurstCompile]
        struct SizeFramingJobOrtho : IJobForEach<
            CM_VcamSizeFramingState, CM_VcamLensState, CM_VcamPositionState,
            CM_VcamRotationState, CM_VcamLookAtTarget, CM_VcamSizeFraming>
        {
            public float deltaTime;
            public float fixedDelta;
            public float aspect;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public void Execute(
                ref CM_VcamSizeFramingState framingState,
                ref CM_VcamLensState lensState,
                [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState,
                [ReadOnly] ref CM_VcamLookAtTarget lookAt,
                [ReadOnly] ref CM_VcamSizeFraming framing)
            {
                if (!targetLookup.TryGetValue(lookAt.target, out CM_TargetSystem.TargetInfo targetInfo))
                    return;

                if (targetInfo.radius < CM_VcamSizeFraming.kMinScreenFitSize)
                    return;

                float h = GetTargetHeight(targetInfo.radius, framing.framingMode, aspect);
                float2 height2 = h / math.max(0.01f, framing.screenFit);
                float dt = math.select(-1, deltaTime, posState.previousFrameDataIsValid);

                float current = lensState.fov * 2;
                float targetHeight = math.select(
                    height2.y,
                    math.select(height2.x, current, current < height2.x),
                    lensState.fov > height2.y);

                var limit = framing.orthoSizeRange * 2;
                targetHeight = math.clamp(targetHeight, limit.x, limit.y);

                // Apply damping
                targetHeight *= 0.5f;
                targetHeight = math.select(
                    targetHeight, framingState.prevFOV + MathHelpers.Damp(
                        targetHeight - framingState.prevFOV, framing.damping, dt),
                    dt >= 0);

                lensState.fov = framingState.prevFOV = targetHeight;
            }
        }
    }
}
