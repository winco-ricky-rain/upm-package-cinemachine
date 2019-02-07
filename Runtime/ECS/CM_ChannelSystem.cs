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

    internal struct CM_ChannelState : ISystemStateComponentData
    {
        public int channel;

        /// Manages the nested blend stack and the camera override frames
        public CM_Blender blender;

        public byte channelIsLive; // GML todo: flags
        public byte orthographic;
        public float aspect;
        public quaternion worldOrientationOverride;
        public float notPlayingTimeModeExpiry;
        public float deltaTime;
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
            ActiveChannelStateJobs.Complete();
            var e = GetChannelState(GetChannelStateIndex(channel)).blender.ActiveVirtualCamera;
            return CM_EntityVcam.GetEntityVcam(e);
        }

        /// <summary>Is there a blend in progress?</summary>
        /// <param name="channel">The CM channel id to check</param>
        public bool IsBlending(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetChannelState(GetChannelStateIndex(channel)).blender.IsBlending;
        }

        /// <summary>
        /// Get the current blend in progress.  May be degenerate, i.e. less than 2 cams
        /// </summary>
        /// <param name="channel">The CM channel id to check</param>
        public CM_BlendState GetActiveBlend(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetChannelState(GetChannelStateIndex(channel)).blender.State;
        }

        /// <summary>Current channel state, final result of all blends</summary>
        /// <param name="channel">The CM channel id to check</param>
        public CameraState GetCurrentCameraState(int channel)
        {
            ActiveChannelStateJobs.Complete();
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
            ActiveChannelStateJobs.Complete();
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
            ActiveChannelStateJobs.Complete();
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
                ActiveChannelStateJobs.Complete();
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
                ActiveChannelStateJobs.Complete();
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
            var state = GetChannelState(index);
            var id = state.blender.SetBlendableOverride(
                overrideId, camA.AsEntity, camB.AsEntity, weightB);
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
                var state = GetChannelState(index);
                state.blender.ReleaseBlendableOverride(overrideId);
                if (state.blender.NumActiveFrames == 0)
                    state.notPlayingTimeModeExpiry = 0;
                SetChannelState(index, state);
            }
        }

        ComponentGroup m_channelsGroup;
        public JobHandle PreActiveChannelStateJobs { get; set; } // GML todo: can we get rid of this?
        public JobHandle ActiveChannelStateJobs { get; private set; }

        protected override void OnCreateManager()
        {
            m_channelsGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Create<CM_ChannelState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var objectCount = m_channelsGroup.CalculateLength();
            var job = new UpdateChannelJob()
            {
                soloCamera = SoloCamera == null ? Entity.Null : SoloCamera.AsEntity,
                channels = m_channelsGroup.GetSharedComponentDataArray<CM_Channel>(),
                channelStates = m_channelsGroup.GetComponentDataArray<CM_ChannelState>()
            };

            // PreUpdate all the channel states
            PreActiveChannelStateJobs.Complete();
            ActiveChannelStateJobs.Complete();
            ActiveChannelStateJobs = new JobHandle();
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

            ActiveChannelStateJobs = job.Schedule(objectCount, 1, h);
            return ActiveChannelStateJobs;
        }


        //[BurstCompile] // GML fixme
        struct UpdateChannelJob : IJobParallelFor
        {
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
                state.blender.Update(state.deltaTime, activeVcam, c.customBlends, c.defaultBlend);
                state.channelIsLive = 0; // set up for next frame
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
    }
}
