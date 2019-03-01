using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamChannel : IComponentData
    {
        public int channel;
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
    public struct CM_VcamLensState : ISystemStateComponentData
    {
        public float fov;
        public float nearClip;
        public float farClip;
        public float dutch;
        public float2 lensShift;

        public byte orthographic;
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
                orthographic = 0,
                aspect = 1
            };
        }
    }


    /// <summary>
    /// Holds the deltaTime with which this vcam needs to be (has been) updated now
    /// </summary>
    [Serializable]
    public struct CM_VcamTimeState : ISystemStateComponentData
    {
        public float deltaTime;
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
    public struct CM_VcamPositionState : ISystemStateComponentData
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
        public byte previousFrameDataIsValid; // GML todo: flags
    }

    [Serializable]
    public struct CM_VcamRotationState : ISystemStateComponentData
    {
        /// <summary>
        /// The world space focus point of the camera.  What the camera wants to look at.
        /// There is a special constant define to represent "nothing".  Be careful to
        /// check for that (or check the HasLookAt property).
        /// </summary>
        public float3 lookAtPoint;

        /// <summary> Raw (un-corrected) world space orientation of this camera </summary>
        public quaternion raw;

        /// <summary>
        /// Rotation correction.  This will be added to the raw orientation.
        /// This value doesn't get fed back into the system when calculating the next frame.
        /// Can be noise, or smoothing, or both, or something else.
        /// </summary>
        public quaternion correction;
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
    public class CM_VcamPreBodySystem : JobComponentSystem
    {
        ComponentGroup m_channelsGroup;
        ComponentGroup m_posGroup;
        ComponentGroup m_rotGroup;
        ComponentGroup m_lensGroup;
        ComponentGroup m_vcamGroup;
        ComponentGroup m_missingPosStateGroup;
        ComponentGroup m_missingRotStateGroup;
        ComponentGroup m_missingLensStateGroup;
        ComponentGroup m_missingTimeStateGroup;

#pragma warning disable 649 // never assigned to
        // Used only to add missing state components
        [Inject] EndFrameBarrier m_missingStateBarrier;
#pragma warning restore 649

        protected override void OnCreateManager()
        {
            m_channelsGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Create<CM_ChannelState>(),
                ComponentType.Create<CM_ChannelBlendState>());
            m_posGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Create<CM_VcamPositionState>());
            m_rotGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Create<CM_VcamRotationState>());
            m_lensGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.ReadOnly<CM_VcamLens>(),
                ComponentType.Create<CM_VcamLensState>(),
                ComponentType.Create<CM_VcamTimeState>());

            m_vcamGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamChannel>(),
                ComponentType.ReadOnly<CM_VcamPriority>(),
                ComponentType.ReadOnly<CM_VcamShotQuality>());

            m_missingPosStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Subtractive<CM_VcamPositionState>());
            m_missingRotStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Subtractive<CM_VcamRotationState>());
            m_missingLensStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Subtractive<CM_VcamLensState>());
            m_missingTimeStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.Subtractive<CM_VcamTimeState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing state components
            var missingPosEntities = m_missingPosStateGroup.GetEntityArray();
            var missingRotEntities = m_missingRotStateGroup.GetEntityArray();
            var missingLensEntities = m_missingLensStateGroup.GetEntityArray();
            var missingTimeEntities = m_missingTimeStateGroup.GetEntityArray();
            if (missingPosEntities.Length + missingRotEntities.Length
                + missingLensEntities.Length + missingTimeEntities.Length > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                for (int i = 0; i < missingPosEntities.Length; ++i)
                    cb.AddComponent(missingPosEntities[i], new CM_VcamPositionState());
                for (int i = 0; i < missingRotEntities.Length; ++i)
                    cb.AddComponent(missingRotEntities[i], new CM_VcamRotationState
                        { raw = quaternion.identity, correction = quaternion.identity });
                for (int i = 0; i < missingLensEntities.Length; ++i)
                    cb.AddComponent(missingLensEntities[i], CM_VcamLensState.FromLens(CM_VcamLens.Default));
                for (int i = 0; i < missingTimeEntities.Length; ++i)
                    cb.AddComponent(missingTimeEntities[i], new CM_VcamTimeState());
            }

            var rotJob = new InitRotJob
            {
                vcamRotations = m_rotGroup.GetComponentDataArray<CM_VcamRotationState>(),
                rotations = GetComponentDataFromEntity<Rotation>(true),
                entities = m_rotGroup.GetEntityArray()
            };
            var rotDeps = rotJob.Schedule(m_rotGroup.CalculateLength(), 32, inputDeps);

            var prioritySystem = World.GetExistingManager<CM_VcamPrioritySystem>();
            var channelStateLookup = prioritySystem.AllocateChannelStateLookup();

            var channelJob = new CacheChannelStateJob
            {
                timeNow = Time.time,
                isPlaying = Application.isPlaying ? 1 : 0,
                channels = m_channelsGroup.GetComponentDataArray<CM_Channel>(),
                channelStates = m_channelsGroup.GetComponentDataArray<CM_ChannelState>(),
                hashMap = channelStateLookup.ToConcurrent()
            };
            channelJob.SetDeltaTimes();
            var channelDeps = channelJob.Schedule(m_channelsGroup.CalculateLength(), 32, inputDeps);

            var blendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>();
            for (int i = 0; i < blendStates.Length; ++i)
            {
                var s = blendStates[i];
                s.priorityQueue.ResetReserved();
                s.blender.PreUpdate();
                blendStates[i] = s;
            }

            var initJob = new InitVcamJob
            {
                lensStates = m_lensGroup.GetComponentDataArray<CM_VcamLensState>(),
                timeStates = m_lensGroup.GetComponentDataArray<CM_VcamTimeState>(),
                vcamPositions = m_posGroup.GetComponentDataArray<CM_VcamPositionState>(),
                positions = GetComponentDataFromEntity<Translation>(true),
                entities = m_posGroup.GetEntityArray(),
                lenses = m_lensGroup.GetComponentDataArray<CM_VcamLens>(),
                vcamChannels = m_lensGroup.GetComponentDataArray<CM_VcamChannel>(),
                channelStates = channelStateLookup
            };
            var initDeps = initJob.Schedule(m_lensGroup.CalculateLength(), 32, channelDeps);

            // Reserve the priority queues
            var reserveDeps = prioritySystem.PreUpdate(channelDeps);

            return JobHandle.CombineDependencies(rotDeps, initDeps, reserveDeps);
        }

        [BurstCompile]
        unsafe struct CacheChannelStateJob : IJobParallelFor
        {
            public float timeNow;
            public int isPlaying;
            public fixed float deltaTimes[5];
            [ReadOnly] public ComponentDataArray<CM_Channel> channels;
            public ComponentDataArray<CM_ChannelState> channelStates;
            public NativeHashMap<int, CM_VcamPrioritySystem.ChannelStates>.Concurrent hashMap;

            public void Execute(int index)
            {
                var c = channels[index];
                var state = channelStates[index];
                state.channel = c.channel;
                state.worldOrientationOverride = math.normalizesafe(c.worldOrientationOverride);
                state.aspect = c.aspect;
                state.orthographic =
                    c.projection == CM_Channel.Projection.Orthographic ? (byte)1 : (byte)0;
                int timeModeIndex = (int)c.timeMode;
                state.deltaTime = math.select(
                    -1, deltaTimes[timeModeIndex],
                    (isPlaying != 0) | (timeNow < state.notPlayingTimeModeExpiry));
                channelStates[index] = state;
                hashMap.TryAdd(state.channel, new CM_VcamPrioritySystem.ChannelStates
                {
                    index = index,
                    state = state
                });
            }

            // Call this from main thread, to access unsafe fixed array
            public void SetDeltaTimes()
            {
                deltaTimes[(int)CM_Channel.TimeMode.DeltaTime] = Time.deltaTime;
                deltaTimes[(int)CM_Channel.TimeMode.DeltaTimeIgnoreScale] = Time.unscaledDeltaTime;
                deltaTimes[(int)CM_Channel.TimeMode.FixedDeltaTime] = Time.fixedTime;
                deltaTimes[(int)CM_Channel.TimeMode.FixedDeltaTimeIgnoreScale] = Time.fixedUnscaledDeltaTime;
                deltaTimes[(int)CM_Channel.TimeMode.Off] = -1;
            }
        }

        [BurstCompile]
        struct InitRotJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamRotationState> vcamRotations;
            [ReadOnly] public ComponentDataFromEntity<Rotation> rotations;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                var r = vcamRotations[index];
                r.correction = quaternion.identity;
                vcamRotations[index] = r;

                var entity = entities[index];
                if (rotations.Exists(entity))
                    r.raw = rotations[entity].Value;

                // GML todo: set the lookAt point if lookAt target
                vcamRotations[index] = r;
            }
        }

        [BurstCompile]
        struct InitVcamJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamLensState> lensStates;
            public ComponentDataArray<CM_VcamTimeState> timeStates;
            public ComponentDataArray<CM_VcamPositionState> vcamPositions;
            [ReadOnly] public ComponentDataFromEntity<Translation> positions;
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public ComponentDataArray<CM_VcamLens> lenses;
            [ReadOnly] public ComponentDataArray<CM_VcamChannel> vcamChannels;
            [ReadOnly] public NativeHashMap<int, CM_VcamPrioritySystem.ChannelStates> channelStates;

            public void Execute(int index)
            {
                if (!channelStates.TryGetValue(vcamChannels[index].channel,
                        out CM_VcamPrioritySystem.ChannelStates channelState))
                    return;

                var lensState = CM_VcamLensState.FromLens(lenses[index]);
                lensState.aspect = channelState.state.aspect;
                lensState.orthographic = channelState.state.orthographic;
                lensStates[index] = lensState;
                timeStates[index] = new CM_VcamTimeState { deltaTime = channelState.state.deltaTime };

                var p = vcamPositions[index];
                p.dampingBypass = float3.zero;
                p.up = math.mul(channelState.state.worldOrientationOverride, math.up());
                var entity = entities[index];
                if (positions.Exists(entity))
                    p.raw = positions[entity].Value;
                vcamPositions[index] = p;
            }
        }
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    public class CM_VcamPreAimSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreAimSystem))]
    public class CM_VcamPreCorrectionSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreCorrectionSystem))]
    public class CM_VcamFinalizeSystem : JobComponentSystem
    {
        ComponentGroup m_posGroup;
        ComponentGroup m_rotGroup;
        ComponentGroup m_transformGroup;

        protected override void OnCreateManager()
        {
            m_posGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamPositionState>());
            m_rotGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamRotationState>(),
                ComponentType.Create<Rotation>());
            m_transformGroup = GetComponentGroup(
                ComponentType.Create<LocalToWorld>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var posJob = new FinalizePosJob
            {
                vcamPositions = m_posGroup.GetComponentDataArray<CM_VcamPositionState>(),
                positions = GetComponentDataFromEntity<Translation>(false),
                entities = m_posGroup.GetEntityArray()
            };
            var posDeps = posJob.Schedule(m_posGroup.CalculateLength(), 32, inputDeps);

            var rotJob = new FinalizeRotJob
            {
                vcamRotations = m_rotGroup.GetComponentDataArray<CM_VcamRotationState>(),
                rotations = m_rotGroup.GetComponentDataArray<Rotation>(),
            };
            var rotDeps = rotJob.Schedule(m_rotGroup.CalculateLength(), 32, inputDeps);

            var transformJob = new PushToTransformJob
            {
                localToWorld = m_transformGroup.GetComponentDataArray<LocalToWorld>(),
                vcamPositions = m_transformGroup.GetComponentDataArray<CM_VcamPositionState>(),
                vcamRotations = m_transformGroup.GetComponentDataArray<CM_VcamRotationState>()
            };
            var transformDeps = transformJob.Schedule(m_transformGroup.CalculateLength(), 32, posDeps);

            return JobHandle.CombineDependencies(rotDeps, transformDeps);
        }

        [BurstCompile]
        struct FinalizePosJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamPositionState> vcamPositions;
            [NativeDisableParallelForRestriction] public ComponentDataFromEntity<Translation> positions;
            [ReadOnly] public EntityArray entities;

            public void Execute(int index)
            {
                var p = vcamPositions[index];
                p.dampingBypass = float3.zero;
                p.previousFrameDataIsValid = 1;
                vcamPositions[index] = p;
                var entity = entities[index];
                if (positions.Exists(entity))
                    positions[entity] = new Translation { Value = p.raw };
            }
        }

        [BurstCompile]
        struct FinalizeRotJob : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<CM_VcamRotationState> vcamRotations;
            [NativeDisableParallelForRestriction] public ComponentDataArray<Rotation> rotations;

            public void Execute(int index)
            {
                rotations[index] = new Rotation { Value = vcamRotations[index].raw };
            }
        }

        [BurstCompile]
        struct PushToTransformJob : IJobParallelFor
        {
            public ComponentDataArray<LocalToWorld> localToWorld;
            [ReadOnly] public ComponentDataArray<CM_VcamPositionState> vcamPositions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotationState> vcamRotations;

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
    }
}
