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
    public struct CM_Channel : IComponentData
    {
        /// <summary>
        /// Each vcam is associated with a specific channel.  Channels each do their
        /// own blending and prioritization.
        /// </summary>
        public int channel;

        /// <summary>
        /// Because cameras need to know which way up is, it's possible to override that
        /// GML todo: this should be an object reference, so value can mutate in state data
        /// </summary>
        public quaternion worldOrientationOverride;

        public enum TimeMode
        {
            DeltaTime,
            DeltaTimeIgnoreScale,
            FixedDeltaTime,
            FixedDeltaTimeIgnoreScale,
            Off
        }
        public TimeMode timeMode;

        /// <summary>Wait this many seconds before activating a new camera</summary>
        public float activateAfter;

        /// <summary>An active camera must be active for at least this many seconds</summary>
        public float minDuration;

        /// <summary>
        /// The blend which is used if you don't explicitly define a blend between two Virtual Cameras.
        /// </summary>
        [CinemachineBlendDefinitionProperty]
        public CinemachineBlendDefinition defaultBlend;
/* GML todo
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
*/
    }

    public struct CM_ChannelState : ISystemStateComponentData
    {
        public int channel;
        public byte orthographic;
        public float aspect;
        public quaternion worldOrientationOverride;
        public float notPlayingTimeModeExpiry;
        public float deltaTime;
        public Entity soloCamera;
        public Entity activeVcam;
    }

    /// Manages the nested blend stack and the camera override frames
    internal struct CM_ChannelBlendState : ISystemStateComponentData
    {
        public CM_Blender blender;

        public float activationTime;
        public float pendingActivationTime;
        public Entity pendingCamera;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPrioritySystem))]
    public class CM_ChannelSystem : JobComponentSystem
    {
        int GetChannelStateIndex(int channel)
        {
            var channels = m_channelsGroup.GetComponentDataArray<CM_Channel>();
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

        CM_ChannelBlendState GetChannelBlendState(int index)
        {
            if (index < 0)
                return new CM_ChannelBlendState();
            var a = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>();
            return a[index];
        }

        void SetChannelBlendState(int index, CM_ChannelBlendState state)
        {
            if (index >= 0)
            {
                var a = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>();
                a[index] = state;
            }
        }

        public Entity GetSoloCamera(int channel)
        {
            return GetChannelState(GetChannelStateIndex(channel)).soloCamera;
        }

        public void SetSoloCamera(int channel, Entity vcam)
        {
            int index = GetChannelStateIndex(channel);
            var s = GetChannelState(index);
            s.soloCamera = vcam;
            SetChannelState(index, s);
        }

        /// <summary>Get the current active virtual camera.</summary>
        /// <param name="channel">The CM channel id to check</param>
        public ICinemachineCamera GetActiveVirtualCamera(int channel)
        {
            ActiveChannelStateJobs.Complete();
            var e = GetChannelBlendState(GetChannelStateIndex(channel)).blender.ActiveVirtualCamera;
            return CM_EntityVcam.GetEntityVcam(e);
        }

        /// <summary>Is there a blend in progress?</summary>
        /// <param name="channel">The CM channel id to check</param>
        public bool IsBlending(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetChannelBlendState(GetChannelStateIndex(channel)).blender.IsBlending;
        }

        /// <summary>
        /// Get the current blend in progress.  May be degenerate, i.e. less than 2 cams
        /// </summary>
        /// <param name="channel">The CM channel id to check</param>
        public CM_BlendState GetActiveBlend(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetChannelBlendState(GetChannelStateIndex(channel)).blender.State;
        }

        /// <summary>Current channel state, final result of all blends</summary>
        /// <param name="channel">The CM channel id to check</param>
        public CameraState GetCurrentCameraState(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetChannelBlendState(GetChannelStateIndex(channel)).blender.State.cameraState;
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
            ActiveChannelStateJobs.Complete();
            return GetChannelBlendState(GetChannelStateIndex(channel)).blender.IsLive(vcam.AsEntity);
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
            ActiveChannelStateJobs.Complete();
            var channels = m_channelsGroup.GetComponentDataArray<CM_Channel>();
            for (int i = 0; i < channels.Length; ++i)
                if (GetChannelBlendState(i).blender.IsLive(vcam.AsEntity))
                    return true;
            return false;
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
                ActiveChannelStateJobs.Complete();
                var state = GetChannelBlendState(index);
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
        /// <param name="timeExpiry">if not playing, time will go off after this time</param>
        /// <param name="camA"> The camera to set, corresponding to weight=0</param>
        /// <param name="camB"> The camera to set, corresponding to weight=1</param>
        /// <param name="weightB">The blend weight.  0=camA, 1=camB</param>
        /// <returns>The oiverride ID.  Don't forget to call ReleaseCameraOverride
        /// after all overriding is finished, to free the OverideStack resources.</returns>
        public int SetCameraOverride(
            int channel, int overrideId, float timeExpiry,
            ICinemachineCamera camA, ICinemachineCamera camB, float weightB)
        {
            var index = GetChannelStateIndex(channel);
            if (index < 0)
                return 0;

            ActiveChannelStateJobs.Complete();
            var blendState = GetChannelBlendState(index);
            var id = blendState.blender.SetBlendableOverride(
                overrideId, camA.AsEntity, camB.AsEntity, weightB);
            SetChannelBlendState(index, blendState);

            // GML todo: something better
            var state = GetChannelState(index);
            state.notPlayingTimeModeExpiry = timeExpiry;
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
                ActiveChannelStateJobs.Complete();
                var blendState = GetChannelBlendState(index);
                blendState.blender.ReleaseBlendableOverride(overrideId);
                if (blendState.blender.NumActiveFrames == 0)
                {
                    var state = GetChannelState(index);
                    state.notPlayingTimeModeExpiry = 0;
                    SetChannelState(index, state);
                }
                SetChannelBlendState(index, blendState);
            }
        }

        ComponentGroup m_channelsGroup;
        ComponentGroup m_missingStateGroup;

#pragma warning disable 649 // never assigned to
        // Used only to add missing state components
        [Inject] EndFrameBarrier m_missingStateBarrier;
#pragma warning restore 649

        JobHandle ActiveChannelStateJobs { get; set; }

        protected override void OnCreateManager()
        {
            m_channelsGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Create<CM_ChannelState>(),
                ComponentType.Create<CM_ChannelBlendState>());
            m_missingStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Subtractive<CM_ChannelBlendState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing state components
            var objectCount = m_missingStateGroup.CalculateLength();
            if (objectCount > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                var missingStateEntities = m_missingStateGroup.GetEntityArray();
                for (int i = 0; i < objectCount; ++i)
                    cb.AddComponent(missingStateEntities[i], new CM_ChannelBlendState());
            }

            objectCount = m_channelsGroup.CalculateLength();
            var updateJob = new UpdateChannelJob()
            {
                now = Time.time,
                channels = m_channelsGroup.GetComponentDataArray<CM_Channel>(),
                channelStates = m_channelsGroup.GetComponentDataArray<CM_ChannelState>(),
                channelBlendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>()
            };

            // PreUpdate all the channel states
            ActiveChannelStateJobs.Complete();
            ActiveChannelStateJobs = new JobHandle();
            for (int i = 0; i < objectCount; ++i)
            {
                var s = updateJob.channelBlendStates[i];
                s.blender.PreUpdate();
                updateJob.channelBlendStates[i] = s;
            }

            JobHandle h = inputDeps;
            var prioritySystem = World.Active.GetExistingManager<CM_VcamPrioritySystem>();
            if (prioritySystem != null)
            {
                h = JobHandle.CombineDependencies(h, prioritySystem.QueueWriteHandle);
                updateJob.queue = prioritySystem.GetPriorityQueueNow(false);
            }
            var updateDeps = updateJob.Schedule(objectCount, 1, h);

            var fetchJob = new FetchActiveVcamJob
            {
                channelStates = m_channelsGroup.GetComponentDataArray<CM_ChannelState>(),
                channelBlendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>()
            };
            ActiveChannelStateJobs = fetchJob.Schedule(objectCount, 1, updateDeps);
            return ActiveChannelStateJobs;
        }


        //[BurstCompile] // GML todo
        struct UpdateChannelJob : IJobParallelFor
        {
            public float now;
            [ReadOnly] public NativeArray<CM_VcamPrioritySystem.QueueEntry> queue;
            [ReadOnly] public ComponentDataArray<CM_Channel> channels;
            [ReadOnly] public ComponentDataArray<CM_ChannelState> channelStates;
            public ComponentDataArray<CM_ChannelBlendState> channelBlendStates;

            public void Execute(int index)
            {
                var c = channels[index];
                float activateAfter = c.activateAfter;
                float minDuration = c.minDuration;

                var state = channelStates[index];
                var blendState = channelBlendStates[index];

                Entity desiredVcam = state.soloCamera;
                if (desiredVcam == Entity.Null)
                    desiredVcam = TopCameraFromPriorityQueue(c.channel);
                else
                    activateAfter = minDuration = 0;


                Entity currentVcam = blendState.blender.ActiveVirtualCamera;
                if (blendState.activationTime != 0)
                {
                    // Is it active now?
                    if (currentVcam == desiredVcam || state.deltaTime < 0)
                    {
                        // Yes, cancel any pending
                        blendState.pendingActivationTime = 0;
                        blendState.pendingCamera = Entity.Null;
                    }

                    // Is it pending?
                    if (blendState.pendingActivationTime != 0
                        && blendState.pendingCamera == desiredVcam)
                    {
                        // Has it not been pending long enough, or are we not allowed to switch away
                        // from the active action?
                        if ((now - blendState.pendingActivationTime) < activateAfter
                            || (now - blendState.activationTime) < minDuration)
                        {
                            desiredVcam = currentVcam; // sorry, not yet
                        }
                    }

                    if (currentVcam != desiredVcam && state.deltaTime >= 0
                        && blendState.pendingCamera != desiredVcam
                        && (activateAfter > 0 || (now - blendState.activationTime) < minDuration))
                    {
                        // Too early - make it pending
                        blendState.pendingCamera = desiredVcam;
                        blendState.pendingActivationTime = now;
                        desiredVcam = currentVcam;
                    }
                }

                if (currentVcam != desiredVcam)
                {
                    blendState.activationTime = now;
                    blendState.pendingActivationTime = 0;
                    blendState.pendingCamera = Entity.Null;
                }

                blendState.blender.Update( // GML todo: fix Update and enable burst
                    state.deltaTime, desiredVcam,
                    null, //c.customBlends,
                    c.defaultBlend);

                channelBlendStates[index] = blendState;
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
                int value = queue[0].vcamChannel.channel;
                if (value == channel)
                    return 0;
                if (value > channel)
                    return -1;

                // Binary search
                int first = 0;
                int mid = first + ((last - first) >> 1);
                while (mid != first)
                {
                    value = queue[mid].vcamChannel.channel;
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

                while (queue[first].vcamChannel.channel != channel)
                    ++first;
                return first;
            }
        }

        struct FetchActiveVcamJob : IJobParallelFor
        {
            public ComponentDataArray<CM_ChannelState> channelStates;
            [ReadOnly] public ComponentDataArray<CM_ChannelBlendState> channelBlendStates;

            public void Execute(int index)
            {
                var s = channelStates[index];
                s.activeVcam = channelBlendStates[index].blender.ActiveVirtualCamera;
                channelStates[index] = s;
            }
        }
    }
}
