using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using System.Collections.Generic;
using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace Cinemachine.ECS
{
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

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    public class CM_VcamPrioritySystem : JobComponentSystem
    {
        JobHandle ActiveChannelStateJobs { get; set; }

        ComponentGroup m_vcamGroup;
        ComponentGroup m_channelsGroup;

        internal struct ChannelStates
        {
            public int index;
            public CM_ChannelState state;
        }
        NativeHashMap<int, ChannelStates> channelStateLookup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetComponentGroup(
                ComponentType.Create<CM_VcamChannel>(),
                ComponentType.ReadOnly<CM_VcamPriority>());
            m_channelsGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.Create<CM_ChannelState>(),
                ComponentType.Create<CM_ChannelBlendState>());

            m_vcamSequence = 1;
        }

        protected override void OnDestroyManager()
        {
            if (channelStateLookup.IsCreated)
                channelStateLookup.Dispose();
            base.OnDestroyManager();
        }

        internal NativeHashMap<int, ChannelStates> AllocateChannelStateLookup()
        {
            var numChannels = m_channelsGroup.CalculateLength();
            if (channelStateLookup.IsCreated)
                channelStateLookup.Dispose();
            channelStateLookup = new NativeHashMap<int, ChannelStates>(numChannels, Allocator.TempJob);
            return channelStateLookup;
        }

        internal JobHandle PreUpdate(JobHandle inputDeps)
        {
            var reserveJob = new ReservePriorityQueueJob
            {
                channels = m_vcamGroup.GetComponentDataArray<CM_VcamChannel>(),
                channelBlendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>(),
                channelLookup = channelStateLookup
            };
            var reserveDeps = reserveJob.Schedule(m_vcamGroup.CalculateLength(), 32, inputDeps);
            ActiveChannelStateJobs = reserveDeps;
            return reserveDeps;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Allocate the priority queues
            ActiveChannelStateJobs.Complete();
            ActiveChannelStateJobs = default;
            var channelBlendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>();
            for (int i = 0; i < channelBlendStates.Length; ++i)
            {
                var s = channelBlendStates[i];
                s.priorityQueue.AllocateReservedQueue();
                channelBlendStates[i] = s;
            }

            var populateJob = new PopulatePriorityQueueJob
            {
                entities = m_vcamGroup.GetEntityArray(),
                channels = m_vcamGroup.GetComponentDataArray<CM_VcamChannel>(),
                priorities = m_vcamGroup.GetComponentDataArray<CM_VcamPriority>(),
                qualities = GetComponentDataFromEntity<CM_VcamShotQuality>(true),
                channelBlendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>(),
                channelLookup = channelStateLookup
            };
            var populateDeps = populateJob.Schedule(m_vcamGroup.CalculateLength(), 32, inputDeps);

            var sortJob = new SortQueueJob
            {
                channels = m_channelsGroup.GetComponentDataArray<CM_Channel>(),
                channelBlendStates = m_channelsGroup.GetComponentDataArray<CM_ChannelBlendState>()
            };
            var sortDeps = sortJob.Schedule(m_channelsGroup.CalculateLength(), 1, populateDeps);
            return sortDeps;
        }

        [BurstCompile]
        struct ReservePriorityQueueJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, ChannelStates> channelLookup;
            [ReadOnly] public ComponentDataArray<CM_VcamChannel> channels;
            [NativeDisableParallelForRestriction] public ComponentDataArray<CM_ChannelBlendState> channelBlendStates;

            public void Execute(int index)
            {
                // GML todo: optimize (get rid of the ifs)
                if (channelLookup.TryGetValue(channels[index].channel, out ChannelStates state))
                {
                    var blendState = channelBlendStates[state.index];
                    blendState.priorityQueue.InterlockedIncrementReserved();
                    channelBlendStates[state.index] = blendState;
                }
            }
        }

        [BurstCompile]
        struct PopulatePriorityQueueJob : IJobParallelFor
        {
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public ComponentDataArray<CM_VcamChannel> channels;
            [ReadOnly] public ComponentDataArray<CM_VcamPriority> priorities;
            [ReadOnly] public ComponentDataFromEntity<CM_VcamShotQuality> qualities;
            [NativeDisableParallelForRestriction] public ComponentDataArray<CM_ChannelBlendState> channelBlendStates;
            [ReadOnly] public NativeHashMap<int, ChannelStates> channelLookup;

            public void Execute(int index)
            {
                // GML todo: optimize (get rid of the ifs)
                if (!channelLookup.TryGetValue(channels[index].channel,
                        out ChannelStates channelState))
                    return;

                var blendState = channelBlendStates[channelState.index];
                var entity = entities[index];
                blendState.priorityQueue.InterlockedAddItem(new CM_PriorityQueue.QueueEntry
                {
                    entity = entity,
                    vcamPriority = priorities[index],
                    shotQuality = qualities.Exists(entity) ? qualities[entity]
                        : new CM_VcamShotQuality { value = CM_VcamShotQuality.DefaultValue }
                });
                channelBlendStates[channelState.index] = blendState;
            }
        }

        [BurstCompile] //GML wtf??? why not?
        unsafe struct SortQueueJob : IJobParallelFor
        {
            [ReadOnly] public ComponentDataArray<CM_Channel> channels;
            public ComponentDataArray<CM_ChannelBlendState> channelBlendStates;

            public void Execute(int index)
            {
                var s = channelBlendStates[index];
                var c = channels[index];

#if false // GML why doesn't burst accept this?
                if (c.sortMode == CM_Channel.SortMode.PriorityThenQuality)
                    s.priorityQueue.Sort(new ComparerPriority());
                else if (c.sortMode == CM_Channel.SortMode.QualityThenPriority)
                    s.priorityQueue.Sort(new ComparerQuality());
#else
                var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<CM_PriorityQueue.QueueEntry>(
                    s.priorityQueue.GetUnsafeDataPtr(), s.priorityQueue.Length, Allocator.None);

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

        int m_vcamSequence = 1;
        public int NextVcamSequence { get { return m_vcamSequence++; } }
    }
}
