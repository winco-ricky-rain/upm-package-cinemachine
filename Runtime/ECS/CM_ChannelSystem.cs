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
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    public class CM_ChannelSystem : ComponentSystem
    {
        [Serializable]
        class ChannelExtraState
        {
            /// Manages the nested blend stack and the camera override frames
            public CM_Blender blender;
            public ICinemachineCamera mActiveCameraPreviousFrame;
        }

        Dictionary<int, ChannelExtraState> mExtraData;

        ChannelExtraState GetExtraData(int channel)
        {
            if (mExtraData == null)
                mExtraData = new Dictionary<int, ChannelExtraState>();
            if (!mExtraData.TryGetValue(channel, out ChannelExtraState extra))
            {
                extra = new ChannelExtraState();
                mExtraData[channel] = extra;
            }
            return extra;
        }

        /// <summary>Called when the current live vcam changes.  If a blend is involved,
        /// then this will be called on the first frame of the blend</summary>
        public delegate void VcamActivatedDelegate(
            ICinemachineCamera newCam, ICinemachineCamera prevCam, bool isCut);

        /// <summary>Called when the current live vcam changes.  If a blend is involved,
        /// then this will be called on the first frame of the blend</summary>
        public VcamActivatedDelegate OnVcamActivated;

        /// <summary>API for the Unity Editor. Show this camera no matter what.</summary>
        public ICinemachineCamera SoloCamera { get; set; }

        /// <summary>Get the current active virtual camera.</summary>
        /// <param name="channel">The CM channel id to check</param>
        public ICinemachineCamera GetActiveVirtualCamera(int channel)
        {
            return GetExtraData(channel).blender.ActiveVirtualCamera;
        }

        /// <summary>Is there a blend in progress?</summary>
        /// <param name="channel">The CM channel id to check</param>
        public bool IsBlending(int channel)
        {
            return GetExtraData(channel).blender.IsBlending;
        }

        /// <summary>
        /// Get the current blend in progress.  May be degenerate, i.e. less than 2 cams
        /// </summary>
        /// <param name="channel">The CM channel id to check</param>
        public CM_Blender.BlendState GetActiveBlend(int channel)
        {
            return GetExtraData(channel).blender.State;
        }

        /// <summary>Current channel state, final result of all blends</summary>
        /// <param name="channel">The CM channel id to check</param>
        public CameraState GetCurrentCameraState(int channel)
        {
            return GetExtraData(channel).blender.State.cameraState;
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
            return GetExtraData(channel).blender.IsLive(vcam);
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
            var channels = m_channelsGroup.GetSharedComponentDataArray<CM_Channel>();
            for (int i = 0; i < channels.Length; ++i)
                if (GetExtraData(channels[i].channel).blender.IsLive(vcam))
                    return true;
            return false;
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
            int channel,
            int overrideId,
            ICinemachineCamera camA, ICinemachineCamera camB,
            float weightB)
        {
            return GetExtraData(channel).blender.SetBlendableOverride(overrideId, camA, camB, weightB);
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
            GetExtraData(channel).blender.ReleaseBlendableOverride(overrideId);
        }

        /// <summary>
        /// Create a quick lookup for channels
        /// </summary>
        public NativeHashMap<int, CM_Channel> CreateChannelsCache(Allocator allocator)
        {
            var channels = m_channelsGroup.GetSharedComponentDataArray<CM_Channel>();
            int len = channels.Length;
            var lookup = new NativeHashMap<int, CM_Channel>(len, allocator);
            for (int i = 0; i < len; ++i)
                lookup.TryAdd(i, channels[i]);
            return lookup;
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

        /// <summary>
        /// Get the highest-priority Enabled ICinemachineCamera
        /// that is visible to my camera.  Culling Mask is used to test visibility.
        /// </summary>
        private ICinemachineCamera TopCameraFromPriorityQueue(int channel)
        {
            var prioritySystem = World.Active.GetExistingManager<CM_VcamPrioritySystem>();
            if (prioritySystem != null)
            {
                int index = prioritySystem.GetChannelStartIndexInQueue(channel);
                var queue = prioritySystem.GetPriorityQueueNow();
                return CM_EntityVcam.GetEntityVcam(queue[index].entity);
            }
            return null;
        }

        private void ProcessActiveCamera(ChannelExtraState extra, float3 worldUp, float deltaTime)
        {
            var activeCamera = extra.blender.ActiveVirtualCamera;

            // Has the current camera changed this frame?
            if (activeCamera != extra.mActiveCameraPreviousFrame)
            {
                // Notify incoming camera of transition
                if (activeCamera != null)
                    activeCamera.OnTransitionFromCamera(extra.mActiveCameraPreviousFrame, worldUp, deltaTime);

                // Send transition notification to observers
                if (OnVcamActivated != null)
                    OnVcamActivated.Invoke(
                        activeCamera, extra.mActiveCameraPreviousFrame, !extra.blender.IsBlending);
            }
            extra.mActiveCameraPreviousFrame = activeCamera;
        }

        ComponentGroup m_channelsGroup;

        protected override void OnCreateManager()
        {
            m_channelsGroup = GetComponentGroup(ComponentType.ReadOnly<CM_Channel>());
        }

        // GML fixme - implement this properly
        protected override void OnUpdate()
        {
            var channels = m_channelsGroup.GetSharedComponentDataArray<CM_Channel>();
            int len = channels.Length;
            for (int i = 0; i < len; ++i)
            {
                var c = channels[i];
                var extra = GetExtraData(c.channel);
                float deltaTime = GetEffectiveDeltaTime(c);
                var activeVcam = SoloCamera ?? TopCameraFromPriorityQueue(c.channel);
                extra.blender.PreUpdate();

                // GML Note: perhaps this can be jobified?  Does it matter?  Probably not.
                extra.blender.Update(deltaTime, activeVcam, c.customBlends, c.defaultBlend);

                // Send activation notifications
                ProcessActiveCamera(extra, math.mul(c.worldOrientationOverride, math.up()), deltaTime);
            }
        }
    }
}
