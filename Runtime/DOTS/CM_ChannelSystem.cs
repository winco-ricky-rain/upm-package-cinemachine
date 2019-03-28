using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

        [Serializable]
        public struct Settings
        {
            /// <summary>
            /// Because cameras need to know which way up is, it's possible to override that
            /// GML todo: this should be an object reference, so value can mutate in state data
            /// </summary>
            public float3 worldPosition;
            public quaternion worldOrientation;

            public float aspect;

            public enum Projection
            {
                Perspective,
                Orthographic
            }
            public Projection projection;

            public bool IsOrthographic { get { return projection == Projection.Orthographic; } }
        }
        public Settings settings;

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
                    settings = new Settings
                    {
                        worldOrientation = quaternion.identity,
                        aspect = 1
                    }
                };
            }
        }
    }

    public struct CM_ChannelState : IComponentData
    {
        public float notPlayingTimeModeExpiry;
        public float deltaTime;
        public Entity soloCamera;
        public Entity activeVcam;
    }

    /// Manages the nested blend stack and the camera override frames
    struct CM_ChannelBlendState : ISystemStateComponentData
    {
        public CM_Blender blender;
        public CM_PriorityQueue priorityQueue;

        public float activationTime;
        public float pendingActivationTime;
        public Entity pendingCamera;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_ChannelSystem : JobComponentSystem
    {
        struct ChannelCache
        {
            public Entity e;
            public CM_Channel c;
            public CM_ChannelState state;
        }
        NativeArray<ChannelCache> channelCache;

        NativeArray<ChannelCache> GetChannelCache(bool recalculate)
        {
            ActiveChannelStateJobs.Complete();
            if (recalculate)
            {
                if (channelCache.IsCreated)
                    channelCache.Dispose();
                var entities = m_channelsGroup.ToEntityArray(Allocator.TempJob);
                channelCache = new NativeArray<ChannelCache>(entities.Length, Allocator.TempJob);
                for (int i = 0; i < entities.Length; ++i)
                {
                    var e = entities[i];
                    channelCache[i] = new ChannelCache
                    {
                        e = e,
                        c = entityManager.GetComponentData<CM_Channel>(e),
                        state = entityManager.GetComponentData<CM_ChannelState>(e)
                    };
                }
                entities.Dispose();
            }
            return channelCache;
        }

        public Entity GetChannelEntity(int channel)
        {
            if (entityManager != null)
            {
                var entities = GetChannelCache(false);
                for (int i = 0; i < entities.Length; ++i)
                {
                    var e = entities[i];
                    if (e.c.channel == channel)
                        return e.e;
                }
            }
            return Entity.Null;
        }

        T GetEntityComponentData<T>(Entity e) where T : struct, IComponentData
        {
            if (e != Entity.Null && entityManager != null)
            {
                if (entityManager.HasComponent<T>(e))
                    return entityManager.GetComponentData<T>(e);
            }
            return new T();
        }

        void SetEntityComponentData<T>(Entity e, T c) where T : struct, IComponentData
        {
            if (e != Entity.Null && entityManager != null)
            {
                if (entityManager.HasComponent<T>(e))
                    entityManager.SetComponentData(e, c);
                else
                    entityManager.AddComponentData(e, c);
            }
        }

        public CM_ChannelState GetChannelState(int channel)
        {
            return GetEntityComponentData<CM_ChannelState>(GetChannelEntity(channel));
        }

        public CM_Channel GetChannelComponent(int channel)
        {
            return GetEntityComponentData<CM_Channel>(GetChannelEntity(channel));
        }

        public Entity GetSoloCamera(int channel)
        {
            return GetEntityComponentData<CM_ChannelState>(GetChannelEntity(channel)).soloCamera;
        }

        public void SetSoloCamera(int channel, Entity vcam)
        {
            var e = GetChannelEntity(channel);
            var s = GetEntityComponentData<CM_ChannelState>(e);
            s.soloCamera = vcam;
            SetEntityComponentData(e, s);
        }

        /// <summary>Get the current active virtual camera.</summary>
        /// <param name="channel">The CM channel id to check</param>
        public ICinemachineCamera GetActiveVirtualCamera(int channel)
        {
            var e = GetChannelEntity(channel);
            var s = GetEntityComponentData<CM_ChannelState>(e);
            return CM_EntityVcam.GetEntityVcam(s.activeVcam);
        }

        /// <summary>Is there a blend in progress?</summary>
        /// <param name="channel">The CM channel id to check</param>
        public bool IsBlending(int channel)
        {
            var e = GetChannelEntity(channel);
            if (GetEntityComponentData<CM_ChannelState>(e).soloCamera != Entity.Null)
                return false;
            return GetEntityComponentData<CM_ChannelBlendState>(e).blender.IsBlending;
        }

        /// <summary>
        /// Get the current blend in progress.  May be degenerate, i.e. less than 2 cams
        /// </summary>
        /// <param name="channel">The CM channel id to check</param>
        public CM_BlendState GetActiveBlend(int channel)
        {
            var e = GetChannelEntity(channel);
            var solo = GetEntityComponentData<CM_ChannelState>(e).soloCamera;
            if (solo != Entity.Null)
                return new CM_BlendState { cam = solo, weight = 1 };
            return GetEntityComponentData<CM_ChannelBlendState>(e).blender.State;
        }

        /// <summary>Current camera state, final result of all blends</summary>
        /// <param name="channel">The CM channel id to check</param>
        public CameraState GetCurrentCameraState(int channel)
        {
            var e = GetChannelEntity(channel);
            var solo = GetEntityComponentData<CM_ChannelState>(e).soloCamera;
            if (solo != Entity.Null)
                return CM_EntityVcam.StateFromEntity(solo);
            return GetEntityComponentData<CM_ChannelBlendState>(e).blender.State.cameraState;
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
            var e = GetChannelEntity(channel);
            var solo = GetEntityComponentData<CM_ChannelState>(e).soloCamera;
            if (solo != Entity.Null && solo == vcam.AsEntity)
                return true;
            return GetEntityComponentData<CM_ChannelBlendState>(e).blender.IsLive(vcam.AsEntity);
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
            var entities = GetChannelCache(false);
            for (int i = 0; i < entities.Length; ++i)
            {
                var e = entities[i];
                if (e.state.soloCamera == vcam.AsEntity)
                    return true;
                if (GetEntityComponentData<CM_ChannelBlendState>(e.e).blender.IsLive(vcam.AsEntity))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get all the vcams that are currently live
        /// </summary>
        /// <param name="channel">Which top-level channel to examine</param>
        /// <param name="vcams">Output the live vcams here</param>
        /// <param name="deep">If true, recurse into the live channels</param>
        public void GetLiveVcams(int channel, List<Entity> vcams, bool deep)
        {
            ActiveChannelStateJobs.Complete();
            var e = GetChannelEntity(channel);
            vcams.Clear();
            var solo = GetEntityComponentData<CM_ChannelState>(e).soloCamera;
            if (solo != Entity.Null)
            {
                vcams.Add(solo);
                return;
            }
            GetEntityComponentData<CM_ChannelBlendState>(e).blender.GetLiveVcams(vcams);
            if (deep && entityManager != null)
            {
                int start = 0;
                int end = vcams.Count;
                while (end > start)
                {
                    for (int i = start; i < end; ++i)
                    {
                        if (entityManager.HasComponent<CM_ChannelBlendState>(vcams[i]))
                        {
                            // Don't add twice
                            bool alreadyAdded = false;
                            for (int j = i - 1; j >= 0 && !alreadyAdded; --j)
                                alreadyAdded = (vcams[j] == vcams[i]);
                            if (alreadyAdded)
                            {
                                vcams.RemoveAt(i--);
                                --end;
                            }
                            else
                            {
                                GetEntityComponentData<CM_ChannelBlendState>(
                                    vcams[i]).blender.GetLiveVcams(vcams);
                            }
                        }
                    }
                    start = end;
                    end = vcams.Count;
                }
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
            var e = GetChannelEntity(channel);
            if (e == Entity.Null)
                return 0;

            var blendState = GetEntityComponentData<CM_ChannelBlendState>(e);
            var id = blendState.blender.SetBlendableOverride(
                overrideId,
                camA == null ? Entity.Null : camA.AsEntity,
                camB == null ? Entity.Null : camB.AsEntity, weightB);
            SetEntityComponentData(e, blendState);

            // GML todo: something better?
            var state = GetEntityComponentData<CM_ChannelState>(e);
            state.notPlayingTimeModeExpiry = Time.time + timeExpiry;
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
            var e = GetChannelEntity(channel);
            var blendState = GetEntityComponentData<CM_ChannelBlendState>(e);
            blendState.blender.ReleaseBlendableOverride(overrideId);
            if (blendState.blender.NumOverrideFrames == 0)
            {
                var state = GetEntityComponentData<CM_ChannelState>(e);
                state.notPlayingTimeModeExpiry = 0;
                SetEntityComponentData(e, state);
            }
            SetEntityComponentData(e, blendState);
        }

        /// <summary>
        /// Initialize the Channel system at the start of the CM pipeline
        /// </summary>
        internal void InitChannelStates()
        {
            CreateMissingStateComponents();
            var channelEntities = GetChannelCache(true);

            float timeNow = Time.time;
            bool isPlaying = Application.isPlaying;

            for (int i = 0; i < channelEntities.Length && entityManager != null; ++i)
            {
                var cache = channelEntities[i];
                var e = cache.e;

                var c = entityManager.GetComponentData<CM_Channel>(e);
                cache.state.deltaTime = -1;
                if (isPlaying || timeNow < cache.state.notPlayingTimeModeExpiry
                        || cache.state.soloCamera != Entity.Null)
                {
                    switch (c.timeMode)
                    {
                        case CM_Channel.TimeMode.DeltaTime: cache.state.deltaTime = Time.deltaTime; break;
                        case CM_Channel.TimeMode.DeltaTimeIgnoreScale: cache.state.deltaTime = Time.unscaledDeltaTime; break;
                        case CM_Channel.TimeMode.FixedDeltaTime: cache.state.deltaTime = Time.fixedTime; break;
                        case CM_Channel.TimeMode.FixedDeltaTimeIgnoreScale: cache.state.deltaTime = Time.fixedUnscaledDeltaTime; break;
                        case CM_Channel.TimeMode.Off: default: cache.state.deltaTime = -1; break;
                    }
                }
                entityManager.SetComponentData(e, cache.state);
                channelEntities[i] = cache;

                var blendState = entityManager.GetComponentData<CM_ChannelBlendState>(e);
                blendState.blender.PreUpdate();
                entityManager.SetComponentData(e, blendState);
            }
        }

        public interface VcamGroupCallback
        {
            JobHandle Invoke(
                ComponentGroup filteredGroup, Entity channelEntity,
                CM_Channel c, CM_ChannelState state, JobHandle inputDeps);
        }

        /// <summary>Invoke a callback for each channel's vcams</summary>
        /// <param name="group">all the vcams</param>
        /// <param name="cb">the callback to invoke per channel</param>
        /// <param name="inputDeps">job handle to pass to callback</param>
        public JobHandle InvokePerVcamChannel<T>(
            ComponentGroup group, JobHandle inputDeps, T cb) where T : struct, VcamGroupCallback
        {
            if (entityManager != null)
            {
                var entities = GetChannelCache(false);
                for (int i = 0; i < entities.Length; ++i)
                {
                    for (int j = 0; j < entities.Length; ++j)
                    {
                        var cache = entities[j];
                        group.SetFilter(new CM_VcamChannel { channel = cache.c.channel });
                        if (group.CalculateLength() > 0)
                            inputDeps = cb.Invoke(group, cache.e, cache.c, cache.state, inputDeps);
                    }
                }
                group.ResetFilter();
            }
            return inputDeps;
        }

        /// <summary>Invoke a callback for each channel's vcams</summary>
        /// <param name="group">all the vcams</param>
        /// <param name="c2">Other shared component for grouping</param>
        /// <param name="cb">the callback to invoke per channel</param>
        /// <param name="inputDeps">job handle to pass to callback</param>
        public JobHandle InvokePerVcamChannel<COMPONENT, CB>(
            ComponentGroup group, JobHandle inputDeps, COMPONENT c2, CB cb)
                where COMPONENT : struct, ISharedComponentData
                where CB : struct, VcamGroupCallback
        {
            if (entityManager != null)
            {
                var entities = GetChannelCache(false);
                for (int i = 0; i < entities.Length; ++i)
                {
                    for (int j = 0; j < entities.Length; ++j)
                    {
                        var cache = entities[j];
                        group.SetFilter(new CM_VcamChannel { channel = cache.c.channel }, c2);
                        if (group.CalculateLength() > 0)
                            inputDeps = cb.Invoke(group, cache.e, cache.c, cache.state, inputDeps);
                    }
                }
                group.ResetFilter();
            }
            return inputDeps;
        }

        /// <summary>
        /// This must be called before getting the active cam and blend state
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="blendLookup"></param>
        public void ResolveUndefinedBlends(int channel, GetBlendDelegate blendLookup)
        {
            ActiveChannelStateJobs.Complete();
            var e = GetChannelEntity(channel);
            var blendState = GetEntityComponentData<CM_ChannelBlendState>(e);

            // Take this opportunity to bump the sequence number of any newly-activated vcam
            MoveVcamToTopOfPrioritySubqueue(blendState.blender.GetNewlyActivatedVcam());

            blendState.blender.ResolveUndefinedBlends(blendLookup);
            SetEntityComponentData(e, blendState);
        }

        public void MoveVcamToTopOfPrioritySubqueue(Entity vcam)
        {
            if (vcam != Entity.Null)
            {
                if (entityManager.HasComponent<CM_VcamPriority>(vcam))
                {
                    var priority = entityManager.GetComponentData<CM_VcamPriority>(vcam);
                    priority.vcamSequence = NextVcamSequence;
                    entityManager.SetComponentData(vcam, priority);
                }
            }
        }

        ComponentGroup m_vcamGroup;
        ComponentGroup m_channelsGroup;
        ComponentGroup m_missingChannelStateGroup;
        ComponentGroup m_missingBlendStateGroup;
        ComponentGroup m_danglingBlendStateGroup;

        EndSimulationEntityCommandBufferSystem m_missingStateBarrier;
        JobHandle ActiveChannelStateJobs { get; set; }
        EntityManager entityManager;

        int m_vcamSequence = 1;
        public int NextVcamSequence { get { return Interlocked.Increment(ref m_vcamSequence); } }

        protected override void OnCreateManager()
        {
            m_channelsGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.ReadWrite<CM_ChannelState>(),
                ComponentType.ReadWrite<CM_ChannelBlendState>());

            m_vcamGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.ReadWrite<CM_VcamPriority>());

            m_missingChannelStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Exclude<CM_ChannelState>());
            m_missingBlendStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Exclude<CM_ChannelBlendState>());

            m_danglingBlendStateGroup = GetComponentGroup(
                ComponentType.Exclude<CM_Channel>(),
                ComponentType.ReadWrite<CM_ChannelBlendState>());

            m_missingStateBarrier = World.GetOrCreateManager<EndSimulationEntityCommandBufferSystem>();
            entityManager = World.GetOrCreateManager<EntityManager>();
            m_vcamSequence = 1;
        }

        protected override void OnDestroyManager()
        {
            DestroyDanglingStateComponents();
            if (channelCache.IsCreated)
                channelCache.Dispose();
            base.OnDestroyManager();
        }

        void CreateMissingStateComponents()
        {
            // Add any missing state components
            if (m_missingChannelStateGroup.CalculateLength() > 0
                || m_missingBlendStateGroup.CalculateLength() > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                var a = m_missingChannelStateGroup.ToEntityArray(Allocator.TempJob);
                for (int i = 0; i < a.Length; ++i)
                    cb.AddComponent(a[i], new CM_ChannelState());
                a.Dispose();
                a = m_missingBlendStateGroup.ToEntityArray(Allocator.TempJob);
                for (int i = 0; i < a.Length; ++i)
                    cb.AddComponent(a[i], new CM_ChannelBlendState());
                a.Dispose();
            }
        }

        void DestroyDanglingStateComponents()
        {
            // Deallocate our resources
            ActiveChannelStateJobs.Complete();
            if (m_danglingBlendStateGroup.CalculateLength() > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                var a = m_danglingBlendStateGroup.ToEntityArray(Allocator.TempJob);
                for (int i = 0; i < a.Length; ++i)
                {
                    var blendState = entityManager.GetComponentData<CM_ChannelBlendState>(a[i]);
                    blendState.blender.Dispose();
                    blendState.priorityQueue.Dispose();
                    entityManager.SetComponentData(a[i], blendState);
                    cb.RemoveComponent<CM_ChannelBlendState>(a[i]);
                    cb.DestroyEntity(a[i]);
                }
                a.Dispose();
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            ActiveChannelStateJobs.Complete();

            // Init the blendstates and populate the queues
            JobHandle populateDeps = InvokePerVcamChannel(
                m_vcamGroup, inputDeps,
                new InitBlendStatesJobLaunch
                    { channelSystem = this, entityManager = entityManager });

            var sortJob = new SortQueueJob();
            var sortDeps = sortJob.ScheduleGroup(m_channelsGroup, populateDeps);

            var updateJob = new UpdateChannelJob() { now = Time.time };
            var updateDeps = updateJob.ScheduleGroup(m_channelsGroup, sortDeps);

            var fetchJob = new FetchActiveVcamJob();
            var fetchDeps = fetchJob.ScheduleGroup(m_channelsGroup, updateDeps);

            ActiveChannelStateJobs = fetchDeps;
            return fetchDeps;
        }

        struct InitBlendStatesJobLaunch : VcamGroupCallback
        {
            public CM_ChannelSystem channelSystem;
            public EntityManager entityManager;
            public JobHandle Invoke(
                ComponentGroup filteredGroup, Entity channelEntity,
                CM_Channel c, CM_ChannelState state, JobHandle inputDeps)
            {
                var blendState = entityManager.GetComponentData<CM_ChannelBlendState>(channelEntity);
                blendState.priorityQueue.AllocateData(filteredGroup.CalculateLength());
                entityManager.SetComponentData(channelEntity, blendState);

                var populateJob = new PopulatePriorityQueueJob
                    { qualities = channelSystem.GetComponentDataFromEntity<CM_VcamShotQuality>(true) };
                populateJob.AssignDataPtr(ref blendState);

                return populateJob.ScheduleGroup(filteredGroup, inputDeps);
            }
        }

        [BurstCompile]
        unsafe struct PopulatePriorityQueueJob : IJobProcessComponentDataWithEntity<CM_VcamPriority>
        {
            [ReadOnly] public ComponentDataFromEntity<CM_VcamShotQuality> qualities;
            [NativeDisableUnsafePtrRestriction] public CM_PriorityQueue.QueueEntry* queue;

            public void AssignDataPtr(ref CM_ChannelBlendState blendState)
            {
                queue = (CM_PriorityQueue.QueueEntry*)blendState.priorityQueue.GetUnsafeDataPtr();
            }

            public void Execute(Entity entity, int index, [ReadOnly] ref CM_VcamPriority priority)
            {
                queue[index] = new CM_PriorityQueue.QueueEntry
                {
                    entity = entity,
                    vcamPriority = priority,
                    shotQuality = qualities.Exists(entity) ? qualities[entity]
                        : new CM_VcamShotQuality { value = CM_VcamShotQuality.DefaultValue }
                };
            }
        }

        [BurstCompile]
        unsafe struct SortQueueJob : IJobProcessComponentData<CM_Channel, CM_ChannelBlendState>
        {
            public void Execute([ReadOnly] ref CM_Channel c, ref CM_ChannelBlendState blendState)
            {
                var data = blendState.priorityQueue.GetUnsafeDataPtr();
                if (data == null || blendState.priorityQueue.Length < 2)
                    return;
                var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<CM_PriorityQueue.QueueEntry>(
                    data, blendState.priorityQueue.Length, Allocator.None);

                #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    var safety = AtomicSafetyHandle.Create();
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, safety);
                #endif

                if (c.sortMode == CM_Channel.SortMode.PriorityThenQuality)
                    array.Sort(new ComparerPriority());
                else if (c.sortMode == CM_Channel.SortMode.QualityThenPriority)
                    array.Sort(new ComparerQuality());

                #if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.Release(safety);
                #endif
            }

            struct ComparerPriority : IComparer<CM_PriorityQueue.QueueEntry>
            {
                public int Compare(CM_PriorityQueue.QueueEntry x, CM_PriorityQueue.QueueEntry y)
                {
                    int p = y.vcamPriority.priority - x.vcamPriority.priority; // high-to-low
                    float qf = y.shotQuality.value - x.shotQuality.value; // high-to-low
                    int q = math.select(-1, (int)math.ceil(qf), qf >= 0);
                    int s = y.vcamPriority.vcamSequence - x.vcamPriority.vcamSequence; // high-to-low
                    int e = y.entity.Index - x.entity.Index;    // high-to-low
                    int v = y.entity.Version - x.entity.Version; // high-to-low
                    return math.select(p,
                        math.select(q,
                            math.select(s,
                                math.select(e, v, e == 0),
                                s == 0),
                            q == 0),
                        p == 0);
                }
            }

            struct ComparerQuality : IComparer<CM_PriorityQueue.QueueEntry>
            {
                public int Compare(CM_PriorityQueue.QueueEntry x, CM_PriorityQueue.QueueEntry y)
                {
                    int p = y.vcamPriority.priority - x.vcamPriority.priority; // high-to-low
                    float qf = y.shotQuality.value - x.shotQuality.value; // high-to-low
                    int q = math.select(-1, (int)math.ceil(qf), qf >= 0);
                    int s = y.vcamPriority.vcamSequence - x.vcamPriority.vcamSequence; // high-to-low
                    int e = y.entity.Index - x.entity.Index;    // high-to-low
                    int v = y.entity.Version - x.entity.Version; // high-to-low
                    return math.select(q,
                        math.select(p,
                            math.select(s,
                                math.select(e, v, e == 0),
                                s == 0),
                            p == 0),
                        q == 0);
                }
            }
        }

        [BurstCompile]
        struct UpdateChannelJob : IJobProcessComponentData<
            CM_Channel, CM_ChannelState, CM_ChannelBlendState>
        {
            public float now;

            public void Execute(
                [ReadOnly] ref CM_Channel c,
                [ReadOnly] ref CM_ChannelState state, ref CM_ChannelBlendState blendState)
            {
                float activateAfter = c.activateAfter;
                float minDuration = c.minDuration;

                Entity desiredVcam = blendState.priorityQueue.EntityAt(0);
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

                blendState.blender.Update(state.deltaTime, desiredVcam);
            }
        }

        [BurstCompile]
        struct FetchActiveVcamJob : IJobProcessComponentData<CM_ChannelState, CM_ChannelBlendState>
        {
            public void Execute(
                ref CM_ChannelState state,
                [ReadOnly] ref CM_ChannelBlendState blendState)
            {
                state.activeVcam = (state.soloCamera != Entity.Null)
                    ? state.soloCamera : blendState.blender.ActiveVirtualCamera;
            }
        }
    }
}
