using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using System.Collections.Generic;
using System;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Transforms;

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
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamPrioritySystem : JobComponentSystem
    {
        JobHandle ActiveChannelStateJobs { get; set; }

        ComponentGroup m_vcamGroup;
        ComponentGroup m_channelsGroup;

        protected override void OnCreateManager()
        {
            m_channelsGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Channel>(),
                ComponentType.ReadWrite<CM_ChannelState>(),
                ComponentType.ReadWrite<CM_ChannelBlendState>());

            m_vcamGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.ReadWrite<CM_VcamPriority>());

            m_vcamSequence = 1;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Allocate the priority queues
            ActiveChannelStateJobs.Complete();
            ActiveChannelStateJobs = default;

            var m = World.GetOrCreateManager<EntityManager>();
            var channelSystem = World.GetOrCreateManager<CM_ChannelSystem>();

            // Init the blendstates and populate the queues
            JobHandle populateDeps = inputDeps;
            channelSystem.InvokePerVcamChannel(
                m_vcamGroup, (ComponentGroup filteredGroup, Entity e, CM_Channel c, CM_ChannelState state) =>
                {
                    var blendState = m.GetComponentData<CM_ChannelBlendState>(e);
                    //blendState.blender.PreUpdate();
                    blendState.priorityQueue.AllocateData(filteredGroup.CalculateLength());
                    m.SetComponentData(e, blendState);

                    var populateJob = new PopulatePriorityQueueJob
                        { qualities = GetComponentDataFromEntity<CM_VcamShotQuality>(true) };
                    populateJob.AssignDataPtr(ref blendState);

                    populateDeps = populateJob.ScheduleGroup(filteredGroup, populateDeps);
                });

            var sortJob = new SortQueueJob();
            var sortDeps = sortJob.ScheduleGroup(m_channelsGroup, populateDeps);

            ActiveChannelStateJobs = sortDeps;
            return sortDeps;
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
                var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<CM_PriorityQueue.QueueEntry>(
                    blendState.priorityQueue.GetUnsafeDataPtr(), blendState.priorityQueue.Length, Allocator.None);

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

        int m_vcamSequence = 1;
        public int NextVcamSequence { get { return m_vcamSequence++; } }
    }
}
