using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_Channel : ISharedComponentData
    {
        /// <summary>
        /// Each vcam is associated with a specific channel.  Channels each do their
        /// own blending and prioritization.
        /// </summary>
        public int channel;

        /// <summary>
        /// Because cameras need to know which way up is, it's possible to override that
        /// </summary>
        public quaternion worldOrientationOverride;

        /// <summary>
        /// When enabled, the cameras will always respond in real-time to user input and damping,
        /// even if the game is running in slow motion
        /// </summary>
        public int ignoreTimeScale;

        /// <summary>
        /// GML todo
        /// </summary>
        public float deltaTimeOveride;

        /// <summary>
        /// The blend which is used if you don't explicitly define a blend between two Virtual Cameras.
        /// </summary>
        [CinemachineBlendDefinitionProperty]
        public CinemachineBlendDefinition defaultBlend;

        /// <summary>
        /// This is the asset which contains custom settings for specific blends.
        /// </summary>
        public CinemachineBlenderSettings customBlends;

        /// <summary>Called when the current live vcam changes.  If a blend is involved,
        /// then this will be called on the first frame of the blend</summary>
        [Serializable] public class ActivationEvent
            : UnityEngine.Events.UnityEvent<ICinemachineCamera, ICinemachineCamera, bool> {}

        /// <summary>Called when the current live vcam changes.  If a blend is involved,
        /// then this will be called on the first frame of the blend</summary>
        public ActivationEvent VcamActivatedEvent;
    }

    struct CM_ChannelState : ISystemStateComponentData
    {
        /// Manages the nested blend stack and the camera override frames
        public CM_Blender blender;
        public int channelIsLive;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPrioritySystem))]
    public class CM_ChannelSystem : JobComponentSystem
    {
        /// <summary>API for the Unity Editor. Show this camera no matter what.</summary>
        public ICinemachineCamera SoloCamera { get; set; }

        int GetChannelStateIndex(int channel)
        {
            var channels = m_channelsGroup.GetSharedComponentDataArray<CM_Channel>();
            int len = channels.Length;
            for (int i = 0; i < len; ++i)
                if (channels[i].channel == channel)
                    return i;
            return -1;
        }

        CM_ChannelState GetChannelState(int index)
        {
            if (index < 0)
                return new CM_ChannelState();
            var a = m_channelsGroup.GetComponentDataArray<CM_ChannelState>();
            return a[index];
        }

        void SetChannelState(int index, CM_ChannelState state)
        {
            if (index >= 0)
            {
                var a = m_channelsGroup.GetComponentDataArray<CM_ChannelState>();
                a[index] = state;
            }
        }

        /// <summary>Get the current active virtual camera.</summary>
        /// <param name="channel">The CM channel id to check</param>
        public ICinemachineCamera GetActiveVirtualCamera(int channel)
        {
            activeChannelStateJobs.Complete();
            var e = GetChannelState(GetChannelStateIndex(channel)).blender.ActiveVirtualCamera;
            return CM_EntityVcam.GetEntityVcam(e);
        }

        /// <summary>Is there a blend in progress?</summary>
        /// <param name="channel">The CM channel id to check</param>
        public bool IsBlending(int channel)
        {
            activeChannelStateJobs.Complete();
            return GetChannelState(GetChannelStateIndex(channel)).blender.IsBlending;
        }

        /// <summary>
        /// Get the current blend in progress.  May be degenerate, i.e. less than 2 cams
        /// </summary>
        /// <param name="channel">The CM channel id to check</param>
        public CM_BlendState GetActiveBlend(int channel)
        {
            activeChannelStateJobs.Complete();
            return GetChannelState(GetChannelStateIndex(channel)).blender.State;
        }

        /// <summary>Current channel state, final result of all blends</summary>
        /// <param name="channel">The CM channel id to check</param>
        public CameraState GetCurrentCameraState(int channel)
        {
            activeChannelStateJobs.Complete();
            return GetChannelState(GetChannelStateIndex(channel)).blender.State.cameraState;
        }

        /// <summary>
        /// True if the ICinemachineCamera is the current active camera
        /// or part of a current blend, either directly or indirectly because its parents are live.
        /// </summary>
        /// <param name="channel">The CM channel id to check</param>
        /// <param name="vcam">The camera to test whether it is live</param>
        /// <returns>True if the camera is live (directly or indirectly)
        /// or part of a blend in progress.</returns>
        public bool IsLive(int channel, ICinemachineCamera vcam)
        {
            activeChannelStateJobs.Complete();
            return GetChannelState(GetChannelStateIndex(channel)).blender.IsLive(vcam.AsEntity);
        }

        /// <summary>
        /// True if the ICinemachineCamera is the current active camera
        /// or part of a current blend, either directly or indirectly because its parents are live.
        /// </summary>
        /// <param name="vcam">The camera to test whether it is live</param>
        /// <returns>True if the camera is live (directly or indirectly)
        /// or part of a blend in progress.</returns>
        public bool IsLive(ICinemachineCamera vcam)
        {
            activeChannelStateJobs.Complete();
            var channels = m_channelsGroup.GetSharedComponentDataArray<CM_Channel>();
            for (int i = 0; i < channels.Length; ++i)
                if (GetChannelState(i).blender.IsLive(vcam.AsEntity))
                    return true;
            return false;
        }

        /// <summary>
        /// Call this every frame that the output of the channel is being rendered to a camera
        /// </summary>
        /// <param name="channel">which chennel to set</param>
        public void SetChannelIsLive(int channel)
        {
            var index = GetChannelStateIndex(channel);
            if (index >= 0)
            {
                activeChannelStateJobs.Complete();
                var state = GetChannelState(index);
                ++state.channelIsLive;
                SetChannelState(index, state);
            }
        }

        /// <summary>
        /// Get all the vcams that are currently live
        /// </summary>
        /// <param name="vcams"></param>
        public void GetLiveVcams(int channel, List<Entity> vcams)
        {
            var index = GetChannelStateIndex(channel);
            if (index >= 0)
            {
                activeChannelStateJobs.Complete();
                var state = GetChannelState(index);
                state.blender.GetLiveVcams(vcams);
            }
        }

        /// <summary>
        /// Override the current camera and current blend.  This setting will trump
        /// any in-game logic that sets virtual camera priorities and Enabled states.
        /// This is the main API for the timeline.
        /// </summary>
        /// <param name="channel">The CM channel id to affect</param>
        /// <param name="overrideId">Id to represent a specific client.  An internal
        /// stack is maintained, with the most recent non-empty override taking precenence.
        /// This id must be > 0.  If you pass -1, a new id will be created, and returned.
        /// Use that id for subsequent calls.  Don't forget to
        /// call ReleaseCameraOverride after all overriding is finished, to
        /// free the OverideStack resources.</param>
        /// <param name="camA"> The camera to set, corresponding to weight=0</param>
        /// <param name="camB"> The camera to set, corresponding to weight=1</param>
        /// <param name="weightB">The blend weight.  0=camA, 1=camB</param>
        /// <param name="deltaTime">override for deltaTime.  Should be Time.FixedDelta for
        /// time-based calculations to be included, -1 otherwise</param>
        /// <returns>The oiverride ID.  Don't forget to call ReleaseCameraOverride
        /// after all overriding is finished, to free the OverideStack resources.</returns>
        public int SetCameraOverride(
            int channel, int overrideId,
            ICinemachineCamera camA, ICinemachineCamera camB, float weightB)
        {
            var index = GetChannelStateIndex(channel);
            if (index < 0)
                return 0;

            activeChannelStateJobs.Complete();
            var state = GetChannelState(index);
            var id = state.blender.SetBlendableOverride(
                overrideId, camA.AsEntity, camB.AsEntity, weightB);
            SetChannelState(index, state);
            return id;
        }

        /// <summary>
        /// Release the resources used for a camera override client.
        /// See SetCameraOverride.
        /// </summary>
        /// <param name="channel">The CM channel id to affect</param>
        /// <param name="overrideId">The ID to released.  This is the value that
        /// was returned by SetCameraOverride</param>
        public void ReleaseCameraOverride(int channel, int overrideId)
        {
            var index = GetChannelStateIndex(channel);
            if (index >= 0)
            {
                activeChannelStateJobs.Complete();
                var state = GetChannelState(index);
                state.blender.ReleaseBlendableOverride(overrideId);
                SetChannelState(index, state);
            }
        }

        /// <summary>
        /// Get an appropriate deltaTime to use when updating vcams on this channel.
        /// Does a linear search for channel info, which may be suboptimal in the
        /// unlikely event that there are a large number of channels.
        /// <param name="channel">The id of the CM channel in question</param>
        /// </summary>
        public float GetEffectiveDeltaTime(int channel)
        {
            // We assume here a small number of channels
            var channels = m_channelsGroup.GetSharedComponentDataArray<CM_Channel>();
            int len = channels.Length;
            for (int i = 0; i < len; ++i)
            {
                var c = channels[i];
                if (c.channel == channel)
                    return GetEffectiveDeltaTime(c);
            }
            return Time.deltaTime;
        }

        /// <summary>
        /// Get an appropriate deltaTime to use when updating vcams on this channel
        /// <param name="channel">The CM channel in question</param>
        /// </summary>
        public float GetEffectiveDeltaTime(CM_Channel channel)
        {
            if (!Application.isPlaying)
            {
                if (SoloCamera != null)
                    return Time.unscaledDeltaTime;
                return -1; // no damping
            }
            return (channel.ignoreTimeScale != 0) ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        ComponentGroup m_channelsGroup;
        ComponentGroup m_missingChannelStateGroup;
        JobHandle activeChannelStateJobs;

#pragma warning disable 649 // never assigned to
        // Used only to add missing CM_VcamTransposerState components
        [Inject] EndFrameBarrier m_missingStateBarrier;
#pragma warning restore 649

        protected override void OnCreateManager()
        {
            m_channelsGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Create<CM_ChannelState>());

            m_missingChannelStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Subtractive<CM_ChannelState>());
        }

        //[BurstCompile]
        struct UpdateChannelJob : IJobParallelFor
        {
            public float deltaTime;
            public float unscaledDeltaTime;
            public int isPlaying;
            public Entity soloCamera;
            [ReadOnly] public NativeArray<CM_VcamPrioritySystem.QueueEntry> queue;
            [ReadOnly] public SharedComponentDataArray<CM_Channel> channels;
            public ComponentDataArray<CM_ChannelState> channelStates;

            public void Execute(int index)
            {
                var c = channels[index];

                Entity activeVcam = soloCamera;
                if (activeVcam == Entity.Null)
                    activeVcam = TopCameraFromPriorityQueue(c.channel);

                var state = channelStates[index];
                state.blender.Update(deltaTime, activeVcam, c.customBlends, c.defaultBlend);
                state.channelIsLive = 0;
                channelStates[index] = state;
            }

            private Entity TopCameraFromPriorityQueue(int channel)
            {
                int index = GetChannelStartIndexInQueue(channel);
                if (index >= 0)
                    return queue[index].entity;
                return Entity.Null;
            }

            // GML todo: performance pass
            private int GetChannelStartIndexInQueue(int channel)
            {
                int last = queue.Length;
                if (last == 0)
                    return -1;

                // Most common case: it's the first one
                int value = queue[0].vcamPriority.channel;
                if (value == channel)
                    return 0;
                if (value > channel)
                    return -1;

                // Binary search
                int first = 0;
                int mid = first + ((last - first) >> 1);
                while (mid != first)
                {
                    value = queue[mid].vcamPriority.channel;
                    if (value < channel)
                    {
                        first = mid;
                        mid = first + ((last - first) >> 1);
                    }
                    else if (value > channel)
                    {
                        last = mid;
                        mid = first + ((last - first) >> 1);
                    }
                    else
                    {
                        last = mid;
                        break;
                    }
                }
                if (value != channel)
                    return -1;

                while (queue[first].vcamPriority.channel != channel)
                    ++first;
                return first;
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing transposer state components
            var objectCount = m_missingChannelStateGroup.CalculateLength();
            if (objectCount > 0)
            {
                var missingEntities = m_missingChannelStateGroup.GetEntityArray();
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                for (int i = 0; i < objectCount; ++i)
                    cb.AddComponent(missingEntities[i], new CM_ChannelState());
            }

            objectCount = m_channelsGroup.CalculateLength();
            var job = new UpdateChannelJob()
            {
                deltaTime = Time.deltaTime,
                unscaledDeltaTime = Time.unscaledDeltaTime,
                isPlaying = Application.isPlaying ? 1 : 0,
                soloCamera = SoloCamera == null ? Entity.Null : SoloCamera.AsEntity,
                channels = m_channelsGroup.GetSharedComponentDataArray<CM_Channel>(),
                channelStates = m_channelsGroup.GetComponentDataArray<CM_ChannelState>()
            };

            // preUpdate all the channel states
            activeChannelStateJobs.Complete();
            activeChannelStateJobs = new JobHandle();
            for (int i = 0; i < objectCount; ++i)
            {
                var s = job.channelStates[i];
                s.blender.PreUpdate();
                job.channelStates[i] = s;
            }

            JobHandle h = inputDeps;
            var prioritySystem = World.Active.GetExistingManager<CM_VcamPrioritySystem>();
            if (prioritySystem != null)
            {
                h = JobHandle.CombineDependencies(h, prioritySystem.QueueWriteHandle);
                job.queue = prioritySystem.GetPriorityQueueNow(false);
            }

            activeChannelStateJobs = job.Schedule(objectCount, 1, h);
            return activeChannelStateJobs;
        }
    }
}
