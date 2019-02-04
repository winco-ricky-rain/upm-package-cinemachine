using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamComposer : IComponentData
    {
        /// <summary>This setting will instruct the composer to adjust its target offset
        /// based on the motion of the target.  The composer will look at a point
        /// where it estimates the target will be this many seconds into the future.
        /// Note that this setting is sensitive to noisy animation, and can amplify
        /// the noise, resulting in undesirable camera jitter.  If the camera jitters
        /// unacceptably when the target is in motion, turn down this setting,
        /// or animate the target more smoothly.</summary>
        [Range(0f, 1f)]
        public float lookaheadTime;

        /// <summary>Controls the smoothness of the lookahead algorithm.
        /// Larger values smooth out jittery predictions and also increase
        /// prediction lag</summary>
        [Range(3, 30)]
        public float lookaheadSmoothing;

        /// <summary>If set, movement along the Y axis will be ignored for
        /// lookahead calculations</summary>
        public byte lookaheadIgnoreY; // GML todo: flags

        /// <summary>Force target to center of screen when this camera activates.
        /// If false, will clamp target to the edges of the dead zone</summary>
        public byte centerOnActivate; // GML todo: flags

        /// <summary>How aggressively the camera tries to follow the target on the screen.
        /// Small numbers are more responsive, rapidly orienting the camera to keep the
        /// target in the dead zone. Larger numbers give a more heavy slowly responding camera.
        /// Using different vertical and horizontal settings can yield a wide range of
        /// camera behaviors.</summary>
        public float2 damping;

        /// <summary>Screen position for target. The camera will rotate to the position
        /// the tracked object here.  Zero is center.</summary>
        public float2 screenPosition;

        /// <summary>Camera will not rotate horizontally if the target is within
        /// this range of the screen position</summary>
        public float2 deadZoneSize;

        /// <summary>When target is within this region, camera will gradually move
        /// to re-align towards the desired position, depending on the damping speed</summary>
        public float2 softZoneSize;

        /// <summary>A non-zero bias will move the targt position away from the center
        /// of the soft zone</summary>
        public float2 softZoneBias;

        /// <summary>Internal API for the inspector editor</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathHelpers.rect2d GetSoftGuideRect()
        {
            return new MathHelpers.rect2d
            {
                pos = screenPosition - (deadZoneSize / 2),
                size = deadZoneSize
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetSoftGuideRect(MathHelpers.rect2d value)
        {
            deadZoneSize = math.clamp(value.size, 0, 1);
            screenPosition = math.clamp(value.pos + value.size/2, -1, 1);
            softZoneSize = math.max(softZoneSize, deadZoneSize);
        }

        /// <summary>Internal API for the inspector editor</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathHelpers.rect2d GetHardGuideRect()
        {
            return new MathHelpers.rect2d
            {
                pos = screenPosition - (softZoneSize / 2)
                    + softZoneBias * (softZoneSize - deadZoneSize),
                size = softZoneSize
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetHardGuideRect(MathHelpers.rect2d value)
        {
            softZoneSize = math.clamp(value.size, 0, 2);
            deadZoneSize = math.min(deadZoneSize, softZoneSize);

            float2 center = value.pos + value.size/2;
            float2 bias = center - screenPosition;
            float2 biasSize = math.max(0, softZoneSize - deadZoneSize);
            softZoneBias = math.select(
                math.clamp(bias / biasSize, -0.5f, 0.5f), 0, biasSize < MathHelpers.Epsilon);
        }
    }

    struct CM_VcamComposerState : ISystemStateComponentData
    {
        public float3 cameraPosPrevFrame;
        public float3 lookAtPrevFrame;
        public float2 screenOffsetPrevFrame;
        public quaternion cameraOrientationPrevFrame;
        //public PositionPredictor predictor = new PositionPredictor(); // GML todo

        // Caching some expensive stuff, all in radians
        public MathHelpers.rect2d fovSoftGuideRect;
        public MathHelpers.rect2d fovHardGuideRect;
        public float2 fov;

        // Maintain the cache.
        // Code lives in the component in order to not expose the underwear.
        float cachedAspect;
        float cachedOrthoSizeOverDistance;
        MathHelpers.rect2d cachedSoftGuideRect;
        MathHelpers.rect2d cachedHardGuideRect;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CacheIsValid(
            bool isOrthographic, float aspect, float vFov,
            MathHelpers.rect2d softGuide, MathHelpers.rect2d hardGuide,
            float targetDistance)
        {
            bool2 rectsAreValid =
                  (softGuide.pos == cachedSoftGuideRect.pos)
                & (softGuide.size == cachedSoftGuideRect.size)
                & (hardGuide.pos == cachedHardGuideRect.pos)
                & (hardGuide.size == cachedHardGuideRect.size);
            bool oodInvalid = (math.abs(math.abs(vFov / targetDistance)
                    - cachedOrthoSizeOverDistance) / cachedOrthoSizeOverDistance)
                        > cachedOrthoSizeOverDistance * 0.01f;
            int orthoDirty = math.select(
                0,
                math.select(math.select(0, 1, oodInvalid), 1, cachedOrthoSizeOverDistance == 0),
                isOrthographic);
            return (aspect == cachedAspect)
                & rectsAreValid.x & rectsAreValid.y
                & (orthoDirty == 0);
        }

        // Called less often - it's expensive
        public void UpdateCache(
            bool isOrthographic, float aspect, float vFov,
            MathHelpers.rect2d softGuide, MathHelpers.rect2d hardGuide,
            float targetDistance)
        {
            if (isOrthographic)
            {
                // Calculate effective fov - fake it for ortho based on target distance
                float ood = math.abs(vFov / targetDistance);
                fov = 2 * new float2(math.atan(aspect * ood), math.atan(ood));
                cachedOrthoSizeOverDistance = ood;
            }
            else
            {
                float radFov = math.radians(vFov);
                fov = new float2(2 * math.tan(radFov/2) * aspect, radFov);
                cachedOrthoSizeOverDistance = 0;
            }
            fovSoftGuideRect = ScreenToFOV(softGuide, fov, aspect);
            cachedSoftGuideRect = softGuide;
            fovHardGuideRect = ScreenToFOV(hardGuide, fov, aspect);
            cachedHardGuideRect = hardGuide;
            cachedAspect = aspect;
        }

        // Convert from screen coords to normalized FOV angular coords
        // GML todo: surely this can be simplified!
        private static MathHelpers.rect2d ScreenToFOV(
            MathHelpers.rect2d rScreen, float2 fov, float aspect)
        {
            var r = rScreen;

            var fwd = new float3(0, 0, 1);
            var left = new float3 (-1, 0, 0);
            var up = new float3(0, 1, 0);

            var persp = math.inverse(float4x4.PerspectiveFov(fov.y, aspect, 0.0001f, 2f));

            var pX = math.mul(persp, new float4((r.pos.x * 2f) - 1f, 0, 0.5f, 1)); pX.z = -pX.z;
            var pY = math.mul(persp, new float4(0, (r.pos.y * 2f) - 1f, 0.5f, 1)); pY.z = -pY.z;

            var angle = new float2(
                MathHelpers.SignedAngleUnit(fwd, math.normalize(pX.xyz), up),
                MathHelpers.SignedAngleUnit(fwd, math.normalize(pY.xyz), left));

            var rMin = ((fov / 2) + angle) /  fov;
            var rMax = r.pos + r.size;

            pX = math.mul(persp, new float4((rMax.x * 2f) - 1f, 0, 0.5f, 1)); pX.z = -pX.z;
            pY = math.mul(persp, new float4(0, (rMax.y * 2f) - 1f, 0.5f, 1)); pY.z = -pY.z;

            angle = new float2(
                MathHelpers.SignedAngleUnit(fwd, math.normalize(pX.xyz), up),
                MathHelpers.SignedAngleUnit(fwd, math.normalize(pY.xyz), left));

            rMax = ((fov / 2) + angle) /  fov;

            return new MathHelpers.rect2d { pos = rMin, size = rMax - rMin };
        }
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreAimSystem))]
    [UpdateBefore(typeof(CM_VcamPreCorrectionSystem))]
    public class CM_VcamComposerSystem : JobComponentSystem
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
                ComponentType.Create<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamLensState>(),
                ComponentType.ReadOnly<CM_VcamLookAtTarget>(),
                ComponentType.ReadOnly<CM_VcamComposer>(),
                ComponentType.Create<CM_VcamComposerState>());

            m_missingStateGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamLensState>(),
                ComponentType.ReadOnly<CM_VcamLookAtTarget>(),
                ComponentType.ReadOnly<CM_VcamComposer>(),
                ComponentType.Subtractive<CM_VcamComposerState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing composer state components
            var missingEntities = m_missingStateGroup.GetEntityArray();
            if (missingEntities.Length > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                for (int i = 0; i < missingEntities.Length; ++i)
                    cb.AddComponent(missingEntities[i], new CM_VcamComposerState());
            }

            var targetSystem = World.GetExistingManager<CM_TargetSystem>();
            var targetLookup = targetSystem.GetTargetLookupForJobs(ref inputDeps);
            if (!targetLookup.IsCreated)
                return default; // no targets yet

            var job = new ComposerJob
            {
                deltaTime = Time.deltaTime, // GML todo: use correct values
                fixedDelta = Time.fixedDeltaTime,
                rotations = m_mainGroup.GetComponentDataArray<CM_VcamRotationState>(),
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPositionState>(),
                lenses = m_mainGroup.GetComponentDataArray<CM_VcamLensState>(),
                targets = m_mainGroup.GetComponentDataArray<CM_VcamLookAtTarget>(),
                composers = m_mainGroup.GetComponentDataArray<CM_VcamComposer>(),
                composerStates = m_mainGroup.GetComponentDataArray<CM_VcamComposerState>(),
                targetLookup = targetLookup
            };
            return targetSystem.RegisterTargetLookupReadJobs(
                job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps));
        }

        [BurstCompile]
        struct ComposerJob : IJobParallelFor
        {
            public float deltaTime;
            public float fixedDelta;
            public ComponentDataArray<CM_VcamRotationState> rotations;
            public ComponentDataArray<CM_VcamComposerState> composerStates;
            [ReadOnly] public ComponentDataArray<CM_VcamPositionState> positions;
            [ReadOnly] public ComponentDataArray<CM_VcamLensState> lenses;
            [ReadOnly] public ComponentDataArray<CM_VcamLookAtTarget> targets;
            [ReadOnly] public ComponentDataArray<CM_VcamComposer> composers;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public void Execute(int index)
            {
                if (!targetLookup.TryGetValue(targets[index].target,
                    out CM_TargetSystem.TargetInfo targetInfo))
                {
                    return;
                }

                var composer = composers[index];
                var rotState = rotations[index];
                var posState = positions[index];
                var composerState = composerStates[index];
                var lensState = lenses[index];
                rotState.lookAtPoint = targetInfo.position;
                var camPos = posState.raw + posState.correction;
                float targetDistance = math.length(rotState.lookAtPoint - camPos);
                if (targetDistance < MathHelpers.Epsilon)
                {
                    if (deltaTime >= 0)
                        rotState.raw = composerState.cameraOrientationPrevFrame;
                    rotations[index] = rotState;
                    return;  // navel-gazing, get outa here
                }

                // Expensive FOV calculations
                if (!composerState.CacheIsValid(
                    lensState.orthographic != 0, lensState.aspect, lensState.fov,
                    composer.GetSoftGuideRect(), composer.GetHardGuideRect(),
                    targetDistance))
                {
                    composerState.UpdateCache(
                        lensState.orthographic != 0, lensState.aspect, lensState.fov,
                        composer.GetSoftGuideRect(), composer.GetHardGuideRect(),
                        targetDistance);
                }

                var rigOrientation = rotState.raw;
                var targetDir = math.normalizesafe(rotState.lookAtPoint - camPos);
                if (deltaTime < 0)
                {
                    // No damping, just snap to central bounds, skipping the soft zone
                    var rect = composerState.fovSoftGuideRect;
                    if (composer.centerOnActivate != 0)
                        rect = new MathHelpers.rect2d { pos = rect.pos + (rect.size / 2), size = float2.zero }; // Force to center
                    RotateToScreenBounds(targetDir, posState.up, rect,
                        ref rigOrientation, composerState.fov, composer.damping, -1);
                }
                else
                {
                    // Start with previous frame's orientation (but with current up)
                    float3 prevDir = composerState.lookAtPrevFrame - (composerState.cameraPosPrevFrame + posState.dampingBypass);
                    float prevDistance = math.length(prevDir);
                    if (prevDistance < MathHelpers.Epsilon)
                        rigOrientation = quaternion.LookRotation(
                            math.mul(composerState.cameraOrientationPrevFrame, new float3(0, 0, 1)), posState.up);
                    else
                    {
                        rigOrientation = quaternion.LookRotation(prevDir / prevDistance, posState.up);
                        rigOrientation = rigOrientation.ApplyCameraRotation(
                            -composerState.screenOffsetPrevFrame, posState.up);
                    }

                    // First force the previous rotation into the hard bounds, no damping,
                    // then move it through the soft zone, with damping
                    RotateToScreenBounds(targetDir, posState.up, composerState.fovHardGuideRect,
                        ref rigOrientation, composerState.fov, composer.damping, -1);
                    RotateToScreenBounds(targetDir, posState.up,composerState.fovSoftGuideRect,
                        ref rigOrientation, composerState.fov, composer.damping, deltaTime);
                }
                composerState.cameraPosPrevFrame = posState.raw + posState.correction;
                composerState.lookAtPrevFrame = rotState.lookAtPoint;
                composerState.cameraOrientationPrevFrame = math.normalize(rigOrientation);
                composerState.screenOffsetPrevFrame = composerState.cameraOrientationPrevFrame.GetCameraRotationToTarget(
                    math.normalizesafe(composerState.lookAtPrevFrame - posState.raw + posState.correction), posState.up);
                composerStates[index] = composerState;

                rotState.raw = composerState.cameraOrientationPrevFrame;
                rotations[index] = rotState;
            }

            //[MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void RotateToScreenBounds(
                float3 targetDir, float3 up, MathHelpers.rect2d screenRect,
                ref quaternion rigOrientation, float2 fov, float2 damping, float deltaTime)
            {
                //Vector3 targetDir = TrackedPoint - state.CorrectedPosition;
                var rotToRect = rigOrientation.GetCameraRotationToTarget(targetDir, up);

                // Bring it to the edge of screenRect, if outside.  Leave it alone if inside.
                ClampVerticalBounds(ref screenRect, targetDir, up, fov.y);
                float min = (screenRect.pos.y) * fov.y;
                float max = (screenRect.pos.y + screenRect.size.y) * fov.y;
                if (rotToRect.x < min)
                    rotToRect.x -= min;
                else if (rotToRect.x > max)
                    rotToRect.x -= max;
                else
                    rotToRect.x = 0;

                min = (screenRect.pos.x) * fov.x;
                max = (screenRect.pos.x + screenRect.size.x) * fov.x;
                if (rotToRect.y < min)
                    rotToRect.y -= min;
                else if (rotToRect.y > max)
                    rotToRect.y -= max;
                else
                    rotToRect.y = 0;

                // Apply damping
                if (deltaTime >= 0)
                {
                    rotToRect.x = MathHelpers.Damp(rotToRect.x, damping.y, deltaTime);
                    rotToRect.y = MathHelpers.Damp(rotToRect.y, damping.x, deltaTime);
                }

                // Rotate
                rigOrientation = rigOrientation.ApplyCameraRotation(rotToRect, up);
            }

            /// <summary>
            /// Prevent upside-down camera situation.  This can happen if we have a high
            /// camera pitch combined with composer settings that cause the camera to tilt
            /// beyond the vertical in order to produce the desired framing.  We prevent this by
            /// clamping the composer's vertical settings so that this situation can't happen.
            /// </summary>
            //[MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void ClampVerticalBounds(ref MathHelpers.rect2d r, float3 dirUnit, float3 up, float fov)
            {
                float angle = MathHelpers.AngleUnit(dirUnit, up);
                float halfFov = (fov / 2f) + 0.01f; // give it a little extra to accommodate precision errors
                if (angle < halfFov)
                {
                    // looking up
                    float maxY = 1f - (halfFov - angle) / fov;
                    if (r.pos.y + r.size.y > maxY)
                    {
                        r.pos.y = math.min(r.pos.y, maxY);
                        r.size.y = Mathf.Min(r.size.y, maxY - r.pos.y);
                        return;
                    }
                }
                float maxAllowed = (float)math.PI - halfFov;
                if (angle > maxAllowed)
                {
                    // looking down
                    float minY = (angle - maxAllowed) / fov;
                    r.pos.y = Mathf.Max(r.pos.y, minY);
                }
            }
        }
    }
}
