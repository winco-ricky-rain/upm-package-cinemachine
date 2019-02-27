using System;
using System.Collections.Generic;
using Unity.Burst;
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

        public float aspect;

        public enum Projection
        {
            Perspective,
            Orthographic
        }
        public Projection projection;

        public enum TimeMode
        {
            DeltaTime,
            DeltaTimeIgnoreScale,
            FixedDeltaTime,
            FixedDeltaTimeIgnoreScale,
            Off
        }
        public TimeMode timeMode;

        public enum SortMode
        {
            PriorityThenQuality,
            QualityThenPriority,
            Custom
        };
        public SortMode sortMode;

        /// <summary>Wait this many seconds before activating a new camera</summary>
        public float activateAfter;

        /// <summary>An active camera must be active for at least this many seconds</summary>
        public float minDuration;

        /// <summary>
        /// The blend which is used if you don't explicitly define a blend between two Virtual Cameras.
        /// </summary>
        [CinemachineBlendDefinitionProperty]
        public CinemachineBlendDefinition defaultBlend;

        public static CM_Channel Default
        {
            get
            {
                return new CM_Channel
                {
                    worldOrientationOverride = quaternion.identity,
                    aspect = 1
                };
            }
        }
    }

    public struct CM_ChannelState : ISystemStateComponentData
    {
        public int channel;
        public quaternion worldOrientationOverride;
        public float notPlayingTimeModeExpiry;
        public float deltaTime;
        public Entity soloCamera;
        public Entity activeVcam;
        public byte orthographic;
        public float aspect;
    }

    /// Manages the nested blend stack and the camera override frames
    internal struct CM_ChannelBlendState : ISystemStateComponentData
    {
        public CM_Blender blender;
        public CM_BlendLookup blendLookup;
        public CM_PriorityQueue priorityQueue;

        public float activationTime;
        public float pendingActivationTime;
        public Entity pendingCamera;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPrioritySystem))]
    public class CM_ChannelSystem : JobComponentSystem
    {
        public Entity GetChannelEntity(int channel)
        {
            var entities = m_channelsGroup.GetEntityArray();
            var channels = m_channelsGroup.GetComponentDataArray<CM_Channel>();
            int len = channels.Length;
            for (int i = 0; i < len; ++i)
                if (channels[i].channel == channel)
                    return entities[i];
            return Entity.Null;
        }

        T GetEntityComponentData<T>(Entity e) where T : struct, IComponentData
        {
            var m = World.Active?.GetExistingManager<EntityManager>();
            if (m != null && e != Entity.Null && m.HasComponent<T>(e))
                return m.GetComponentData<T>(e);
            return new T();
        }

        void SetEntityComponentData<T>(Entity e, T c) where T : struct, IComponentData
        {
            var m = World.Active?.GetExistingManager<EntityManager>();
            if (m != null && e != Entity.Null)
            {
                if (m.HasComponent<T>(e))
                    m.SetComponentData(e, c);
                else
                    m.AddComponentData(e, c);
            }
        }

        public CM_ChannelState GetChannelState(int channel)
        {
            return GetEntityComponentData<CM_ChannelState>(GetChannelEntity(channel));
        }

        public Entity GetSoloCamera(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetEntityComponentData<CM_ChannelState>(GetChannelEntity(channel)).soloCamera;
        }

        public void SetSoloCamera(int channel, Entity vcam)
        {
            ActiveChannelStateJobs.Complete();
            var e = GetChannelEntity(channel);
            var s = GetEntityComponentData<CM_ChannelState>(e);
            s.soloCamera = vcam;
            SetEntityComponentData<CM_ChannelState>(e, s);
        }

        /// <summary>Get the current active virtual camera.</summary>
        /// <param name="channel">The CM channel id to check</param>
        public ICinemachineCamera GetActiveVirtualCamera(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return CM_EntityVcam.GetEntityVcam(
                GetEntityComponentData<CM_ChannelState>(GetChannelEntity(channel)).activeVcam);
        }

        /// <summary>Is there a blend in progress?</summary>
        /// <param name="channel">The CM channel id to check</param>
        public bool IsBlending(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetEntityComponentData<CM_ChannelBlendState>(
                GetChannelEntity(channel)).blender.IsBlending;
        }

        /// <summary>
        /// Get the current blend in progress.  May be degenerate, i.e. less than 2 cams
        /// </summary>
        /// <param name="channel">The CM channel id to check</param>
        public CM_BlendState GetActiveBlend(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetEntityComponentData<CM_ChannelBlendState>(
                GetChannelEntity(channel)).blender.State;
        }

        /// <summary>Current camera state, final result of all blends</summary>
        /// <param name="channel">The CM channel id to check</param>
        public CameraState GetCurrentCameraState(int channel)
        {
            ActiveChannelStateJobs.Complete();
            return GetEntityComponentData<CM_ChannelBlendState>(
                GetChannelEntity(channel)).blender.State.cameraState;
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
            return GetEntityComponentData<CM_ChannelBlendState>(
                GetChannelEntity(channel)).blender.IsLive(vcam.AsEntity);
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
            var entities = m_channelsGroup.GetEntityArray();
            int len = entities.Length;
            for (int i = 0; i < len; ++i)
                if (GetEntityComponentData<CM_ChannelBlendState>(entities[i]).blender.IsLive(vcam.AsEntity))
                    return true;
            return false;
        }

        /// <summary>
        /// Get all the vcams that are currently live
        /// </summary>
        /// <param name="vcams"></param>
        public void GetLiveVcams(int channel, List<Entity> vcams)
        {
            ActiveChannelStateJobs.Complete();
            GetEntityComponentData<CM_ChannelBlendState>(
                GetChannelEntity(channel)).blender.GetLiveVcams(vcams);
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
            var e = GetChannelEntity(channel);
            if (e == Entity.Null)
                return 0;

            ActiveChannelStateJobs.Complete();
            var blendState = GetEntityComponentData<CM_ChannelBlendState>(e);
            var id = blendState.blender.SetBlendableOverride(
                overrideId, camA.AsEntity, camB.AsEntity, weightB);
            SetEntityComponentData(e, blendState);

            // GML todo: something better
            var state = GetEntityComponentData<CM_ChannelState>(e);
            state.notPlayingTimeModeExpiry = timeExpiry;
            SetEntityComponentData(e, state);

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
            ActiveChannelStateJobs.Complete();
            var e = GetChannelEntity(channel);
            var blendState = GetEntityComponentData<CM_ChannelBlendState>(e);
            blendState.blender.ReleaseBlendableOverride(overrideId);
            if (blendState.blender.NumActiveFrames == 0)
            {
                var state = GetEntityComponentData<CM_ChannelState>(e);
                state.notPlayingTimeModeExpiry = 0;
                SetEntityComponentData(e, state);
            }
            SetEntityComponentData(e, blendState);
        }

        // GML todo: this is evil, think of something
        public void BuildBlendLookup(
            int channel, CinemachineBlenderSettings bendDefs,
            List<ICinemachineCamera> allVcams)
        {
            // Hash the vcams based on the names
            Dictionary<string, List<ICinemachineCamera>> nameLookup
                = new Dictionary<string, List<ICinemachineCamera>>();
            for (int i = 0; i < allVcams.Count; ++i)
            {
                var vcam = allVcams[i];
                var name = vcam.Name;
                if (!nameLookup.TryGetValue(name, out List<ICinemachineCamera> list))
                {
                    list = new List<ICinemachineCamera>();
                    nameLookup.Add(name, list);
                }
                list.Add(vcam);
                nameLookup[name] = list;
            }

            ActiveChannelStateJobs.Complete();
            var e = GetChannelEntity(channel);
            var blendState = GetEntityComponentData<CM_ChannelBlendState>(e);
            int numBlends = bendDefs == null ? 0 : bendDefs.m_CustomBlends.Length;
            blendState.blendLookup.Reset(numBlends);

            List<Entity> from = new List<Entity>();
            List<Entity> to = new List<Entity>();
            for (int i = 0; i < numBlends; ++i)
            {
                var src = bendDefs.m_CustomBlends[i];
                List<ICinemachineCamera> list;

                from.Clear();
                if (src.m_From == CinemachineBlenderSettings.kBlendFromAnyCameraLabel)
                    from.Add(Entity.Null);
                else if (nameLookup.TryGetValue(src.m_From, out list))
                {
                    for (int j = 0; j < list.Count; ++j)
                        from.Add(list[j].AsEntity);
                }

                to.Clear();
                if (src.m_To == CinemachineBlenderSettings.kBlendFromAnyCameraLabel)
                    to.Add(Entity.Null);
                else if (nameLookup.TryGetValue(src.m_To, out list))
                {
                    for (int j = 0; j < list.Count; ++j)
                        to.Add(list[j].AsEntity);
                }

                // Create the blends
                for (int x = 0; x < from.Count; ++x)
                {
                    for (int y = 0; y < to.Count; ++y)
                    {
                        blendState.blendLookup.AddBlendToLookup(
                            from[x], to[y], new CM_BlendLookup.BlendDef
                            {
                                curve = src.m_Blend.BlendCurve,
                                duration = math.select(
                                    src.m_Blend.m_Time, 0,
                                    src.m_Blend.m_Style == CinemachineBlendDefinition.Style.Cut)
                            });
                    }
                }
            }
            SetEntityComponentData(e, blendState);
        }


        ComponentGroup m_channelsGroup;
        ComponentGroup m_missingChannelStateGroup;
        ComponentGroup m_missingBlendStateGroup;

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
            m_missingChannelStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Subtractive<CM_ChannelState>());
            m_missingBlendStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Subtractive<CM_ChannelBlendState>());
        }

        protected override void OnDestroyManager()
        {
            var blendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>();
            for (int i = 0; i < blendStates.Length; ++i)
            {
                blendStates[i].blender.Dispose();
                blendStates[i].blendLookup.Dispose();
                blendStates[i].priorityQueue.Dispose();
            }
            base.OnDestroyManager();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing state components
            var missingChannelStateEntities = m_missingChannelStateGroup.GetEntityArray();
            var missingBlendStateEntities = m_missingBlendStateGroup.GetEntityArray();
            if (missingChannelStateEntities.Length + missingBlendStateEntities.Length > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                for (int i = 0; i < missingChannelStateEntities.Length; ++i)
                    cb.AddComponent(missingChannelStateEntities[i], new CM_ChannelState { aspect = 1 });
                for (int i = 0; i < missingBlendStateEntities.Length; ++i)
                    cb.AddComponent(missingBlendStateEntities[i], new CM_ChannelBlendState());
            }

            var objectCount = m_channelsGroup.CalculateLength();
            var updateJob = new UpdateChannelJob()
            {
                now = Time.time,
                channels = m_channelsGroup.GetComponentDataArray<CM_Channel>(),
                channelStates = m_channelsGroup.GetComponentDataArray<CM_ChannelState>(),
                channelBlendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>()
            };
            var updateDeps = updateJob.Schedule(objectCount, 1, inputDeps);

            var fetchJob = new FetchActiveVcamJob
            {
                channelStates = m_channelsGroup.GetComponentDataArray<CM_ChannelState>(),
                channelBlendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>()
            };
            var fetchDeps = fetchJob.Schedule(objectCount, 1, updateDeps);
            ActiveChannelStateJobs = fetchDeps;
            return fetchDeps;
        }


        [BurstCompile]
        struct UpdateChannelJob : IJobParallelFor
        {
            public float now;
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
                    desiredVcam = blendState.priorityQueue.EntityAt(0);
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

                blendState.blender.Update(
                    state.deltaTime, desiredVcam,
                    blendState.blendLookup,
                    new CM_BlendLookup.BlendDef
                    {
                        curve = c.defaultBlend.BlendCurve,
                        duration = math.select(
                            c.defaultBlend.m_Time, 0,
                            c.defaultBlend.m_Style == CinemachineBlendDefinition.Style.Cut)
                    });

                channelBlendStates[index] = blendState;
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
