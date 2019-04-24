using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System;
using System.Runtime.CompilerServices;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamChannel : ISharedComponentData
    {
        public int channel;
    }

    [Serializable]
    public struct CM_VcamPriority : IComponentData
    {
        /// <summary>The priority will determine which camera becomes active based on the
        /// state of other cameras and this camera.  Higher numbers have greater priority.
        /// </summary>
        public int priority;

        /// <summary>Used as second key for priority sorting</summary>
        public int vcamSequence;
    }

    [Serializable]
    public struct CM_VcamFollowTarget : IComponentData
    {
        public Entity target;
    }

    [Serializable]
    public struct CM_VcamLookAtTarget : IComponentData
    {
        public Entity target;
    }

    /// <summary>
    /// Describes the FOV and clip planes for a camera.  This generally mirrors the Unity Camera's
    /// lens settings, and will be used to drive the Unity camera when the vcam is active.
    /// </summary>
    [Serializable]
    public struct CM_VcamLens : IComponentData
    {
        /// <summary>
        /// This is the camera view in vertical degrees. For cinematic people, a 50mm lens
        /// on a super-35mm sensor would equal a 19.6 degree FOV.
        /// When using an orthographic camera, this defines the height, in world
        /// co-ordinates, of the camera view.
        /// </summary>
        public float fov;

        /// <summary> The near clip plane for this LensSettings </summary>
        public float nearClip;

        /// <summary> The far clip plane for this LensSettings </summary>
        public float farClip;

        /// <summary> The dutch (tilt) to be applied to the camera. In degrees </summary>
        public float dutch;

        /// <summary> For physical cameras only: position of the gate relative to the film back </summary>
        public float2 lensShift;

        public static CM_VcamLens Default
        {
            get { return new CM_VcamLens { fov = 40, nearClip = 0.1f, farClip = 5000 }; }
        }
    }

    /// <summary>
    /// Exactly the same thing as CM_VcamLens, but mutable.
    /// GML todo: yuck
    /// </summary>
    [Serializable]
    public struct CM_VcamLensState : IComponentData
    {
        public float fov;
        public float nearClip;
        public float farClip;
        public float dutch;
        public float2 lensShift;

        public bool orthographic;
        public float aspect;

        public static CM_VcamLensState FromLens(CM_VcamLens v)
        {
            return new CM_VcamLensState
            {
                fov = v.fov,
                nearClip = v.nearClip,
                farClip = v.farClip,
                dutch = v.dutch,
                lensShift = v.lensShift,
                orthographic = false,
                aspect = 1
            };
        }
    }

    [Serializable]
    public struct CM_VcamBlendHint : IComponentData
    {
        /// <summary>
        /// These hints can be or'ed toether to influence how blending is done, and how state
        /// is applied to the camera
        /// </summary>
        public enum BlendHintValue
        {
            /// <summary>Normal state blending</summary>
            Nothing = 0,
            /// <summary>This state does not affect the camera position</summary>
            NoPosition = 1,
            /// <summary>This state does not affect the camera rotation</summary>
            NoOrientation = 2,
            /// <summary>Combination of NoPosition and NoOrientation</summary>
            NoTransform = NoPosition | NoOrientation,
            /// <summary>Spherical blend about the LookAt target (if any)</summary>
            SphericalPositionBlend = 4,
            /// <summary>Cylindrical blend about the LookAt target (if any)</summary>
            CylindricalPositionBlend = 8,
            /// <summary>Radial blend when the LookAt target changes(if any)</summary>
            RadialAimBlend = 16,
            /// <summary>Ignore the LookAt target and just slerp the orientation</summary>
            IgnoreLookAtTarget = 32,
            /// <summary>This state does not affect the lens</summary>
            NoLens = 64,
        }

        /// <summary>
        /// These hints can be or'ed toether to influence how blending is done, and how state
        /// is applied to the camera
        /// </summary>
        public BlendHintValue blendHint;
    }

    [Serializable]
    public struct CM_VcamPositionState : IComponentData
    {
        /// <summary> Raw (un-corrected) world space position of this camera </summary>
        public float3 raw;

        /// <summary>
        /// Position correction.  This will be added to the raw position.
        /// This value doesn't get fed back into the system when calculating the next frame.
        /// Can be noise, or smoothing, or both, or something else.
        /// </summary>
        public float3 correction;

        /// <summary>This is a way for the Body component to bypass aim damping,
        /// useful for when the body needs to rotate its point of view, but does not
        /// want interference from the aim damping</summary>
        public float3 dampingBypass;

        /// <summary> Which way is up for this vcam (independent of its orientation).  World space unit vector. </summary>
        public float3 up;

        /// GML not sure where to put this
        public bool previousFrameDataIsValid; // GML todo: flags
        public bool isLive;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 GetCorrected() { return raw + correction; }
    }

    [Serializable]
    public struct CM_VcamRotationState : IComponentData
    {
        /// <summary>
        /// The world space focus point of the camera.  What the camera wants to look at.
        /// There is a special constant define to represent "nothing".  Be careful to
        /// check for that (or check the HasLookAt property).
        /// </summary>
        public float3 lookAtPoint;

        public float lookAtRadius;

        /// <summary> Raw (un-corrected) world space orientation of this camera </summary>
        public quaternion raw;

        /// <summary>
        /// Rotation correction.  This will be added to the raw orientation.
        /// This value doesn't get fed back into the system when calculating the next frame.
        /// Can be noise, or smoothing, or both, or something else.
        /// </summary>
        public quaternion correction;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public quaternion GetCorrected() { return math.mul(raw, correction); }
    }

    /// <summary>
    /// Subjective estimation of how "good" the shot is.
    /// Larger values mean better quality.  Default is 1.
    /// </summary>
    [Serializable]
    public struct CM_VcamShotQuality : IComponentData
    {
        public const float DefaultValue = 1;
        public float value;
    }

    // These systems define the CM Vcam pipeline, in this order.
    // Use them to ensure correct ordering of CM pipeline systems

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_TargetSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamPreBodySystem : JobComponentSystem
    {
        EntityQuery m_rotGroup;
        EntityQuery m_vcamGroup;
        EntityQuery m_missingPosStateGroup;
        EntityQuery m_missingRotStateGroup;
        EntityQuery m_missingLensStateGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetEntityQuery(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.ReadOnly<CM_VcamLens>(),
                ComponentType.ReadWrite<CM_VcamLensState>(),
                ComponentType.ReadWrite<CM_VcamPositionState>(),
                ComponentType.ReadWrite<CM_VcamRotationState>());

            m_missingPosStateGroup = GetEntityQuery(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Exclude<CM_VcamPositionState>());
            m_missingRotStateGroup = GetEntityQuery(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Exclude<CM_VcamRotationState>());
            m_missingLensStateGroup = GetEntityQuery(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Exclude<CM_VcamLensState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing state components
            EntityManager.AddComponent(m_missingPosStateGroup,
                    ComponentType.ReadWrite<CM_VcamPositionState>());
            EntityManager.AddComponent(m_missingRotStateGroup,
                    ComponentType.ReadWrite<CM_VcamRotationState>());
            EntityManager.AddComponent(m_missingLensStateGroup,
                ComponentType.ReadWrite<CM_VcamLensState>());

            var channelSystem = World.GetOrCreateSystem<CM_ChannelSystem>();
            channelSystem.InitChannelStates();
            JobHandle vcamDeps = channelSystem.InvokePerVcamChannel(
                m_vcamGroup, inputDeps,
                new InitVcamJobLaunch { system = this });
            return vcamDeps;
        }

        struct InitVcamJobLaunch : CM_ChannelSystem.VcamGroupCallback
        {
            public CM_VcamPreBodySystem system;
            public JobHandle Invoke(
                EntityQuery filteredGroup, Entity channelEntity,
                CM_Channel c, CM_ChannelState state, JobHandle inputDeps)
            {
                var initJob = new InitVcamJob
                {
                    channelSettings = c.settings,
                    up = math.mul(c.settings.worldOrientation, math.up()),
                    orthographic = c.settings.projection == CM_Channel.Settings.Projection.Orthographic,
                    positions = system.GetComponentDataFromEntity<LocalToWorld>(true)
                };
                return initJob.Schedule(filteredGroup, inputDeps);
            }
        }

        [BurstCompile]
        struct InitVcamJob : IJobForEachWithEntity<
            CM_VcamLensState, CM_VcamPositionState, CM_VcamRotationState, CM_VcamLens>
        {
            public CM_Channel.Settings channelSettings;
            public float3 up;
            public bool orthographic;
            [ReadOnly] public ComponentDataFromEntity<LocalToWorld> positions;

            public void Execute(
                Entity entity, int index,
                ref CM_VcamLensState lensState,
                ref CM_VcamPositionState posState,
                ref CM_VcamRotationState rotState,
                [ReadOnly] ref CM_VcamLens lens)
            {
                lensState = CM_VcamLensState.FromLens(lens);
                lensState.aspect = channelSettings.aspect;
                lensState.orthographic = orthographic;

                posState.dampingBypass = float3.zero;
                posState.up = up;
                posState.correction = float3.zero;
                rotState.correction = quaternion.identity;
                if (positions.Exists(entity))
                {
                    var m = positions[entity].Value;
                    posState.raw = m.GetTranslationFromTRS();
                    rotState.raw = quaternion.LookRotation(math.rotate(m, new float3(0, 0, 1)), up);
                }
                // GML todo: set the lookAt point if lookAt target
            }
        }
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamPreAimSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreAimSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamPreCorrectionSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreCorrectionSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamFinalizeSystem : JobComponentSystem
    {
        EntityQuery m_posGroup;
        EntityQuery m_transformGroup;

        protected override void OnCreateManager()
        {
            m_posGroup = GetEntityQuery(
                ComponentType.ReadWrite<CM_VcamPositionState>());
            m_transformGroup = GetEntityQuery(
                ComponentType.ReadWrite<LocalToWorld>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>(),
                ComponentType.Exclude<CM_Channel>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var posJob = new FinalizePosJob();
            var posDeps = posJob.Schedule(m_posGroup, inputDeps);

            var transformJob = new PushToTransformJob();
            var transformDeps = transformJob.Schedule(m_transformGroup, posDeps);

            return transformDeps;
        }

        [BurstCompile]
        struct FinalizePosJob : IJobForEachWithEntity<CM_VcamPositionState>
        {
            public void Execute(Entity entity, int index, ref CM_VcamPositionState posState)
            {
                posState.previousFrameDataIsValid = true;
                posState.isLive = false;
            }
        }

        [BurstCompile]
        struct PushToTransformJob : IJobForEach<
            CM_VcamPositionState, CM_VcamRotationState, LocalToWorld>
        {
            public void Execute(
                [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState,
                ref LocalToWorld l2w)
            {
                // Preserve the scale
                l2w.Value = float4x4.TRS(posState.raw, rotState.raw, l2w.Value.GetScaleFromTRS());
            }
        }
    }
}
