using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamPriority : IComponentData
    {
        /// <summary>Like GameObjet layer, brain will only see the vcams that pass
        /// its channel filter</summary>
        public int channel;

        /// <summary>The priority will determine which camera becomes active based on the
        /// state of other cameras and this camera.  Higher numbers have greater priority.
        /// </summary>
        public int priority;

        /// <summary>Used as second key for priority sorting</summary>
        public int vcamSequence;
    }

    // GML todo: use shared component for channel and parallelize sort
    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    public class CM_VcamPrioritySystem : JobComponentSystem
    {
        ComponentGroup m_groupA;
        ComponentGroup m_groupB;

        protected override void OnCreateManager()
        {
            m_groupA = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamPriority>(),
                ComponentType.Subtractive<CM_VcamShotQuality>());

            m_groupB = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamPriority>(),
                ComponentType.ReadOnly<CM_VcamShotQuality>());

            m_vcamSequence = 1;
            m_priorityQueue = new NativeArray<QueueEntry>(16, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            m_priorityQueue.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Make sure all readers have finished with the queue
            QueueReadJobHandle.Complete();
            QueueReadJobHandle = default(JobHandle);

            var lengthA = m_groupA.CalculateLength();
            var lengthB = m_groupB.CalculateLength();
            if (m_priorityQueue.Length != lengthA + lengthB)
            {
                m_priorityQueue.Dispose();
                m_priorityQueue = new NativeArray<QueueEntry>(lengthA + lengthB, Allocator.Persistent);
            }

            var depsA = new PopulateQueueDefaultQualityJob
            {
                entities = m_groupA.GetEntityArray(),
                priorities = m_groupA.GetComponentDataArray<CM_VcamPriority>(),
                priorityQueue = m_priorityQueue
            }.Schedule(lengthA, 32, inputDeps);

            var depsB = new PopulateQueueJob
            {
                entities = m_groupB.GetEntityArray(),
                priorities = m_groupB.GetComponentDataArray<CM_VcamPriority>(),
                qualities = m_groupB.GetComponentDataArray<CM_VcamShotQuality>(),
                priorityQueue = m_priorityQueue,
                arrayOffset = lengthA
            }.Schedule(lengthB, 32, inputDeps);

            var depsC = JobHandle.CombineDependencies(depsA, depsB);
            QueueWriteHandle = new SortQueueJob { priorityQueue = m_priorityQueue }.Schedule(1, 1, depsC);
            return QueueWriteHandle;
        }

        [BurstCompile]
        struct PopulateQueueDefaultQualityJob : IJobParallelFor
        {
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public ComponentDataArray<CM_VcamPriority> priorities;
            public NativeArray<QueueEntry> priorityQueue;

            public void Execute(int index)
            {
                priorityQueue[index] = new QueueEntry
                {
                    entity = entities[index],
                    vcamPriority = priorities[index],
                    shotQuality = new CM_VcamShotQuality { value = CM_VcamShotQuality.DefaultValue }
                };
            }
        }

        [BurstCompile]
        struct PopulateQueueJob : IJobParallelFor
        {
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public ComponentDataArray<CM_VcamPriority> priorities;
            [ReadOnly] public ComponentDataArray<CM_VcamShotQuality> qualities;
            public NativeArray<QueueEntry> priorityQueue;
            public int arrayOffset;

            public void Execute(int index)
            {
                priorityQueue[index + arrayOffset] = new QueueEntry
                {
                    entity = entities[index],
                    vcamPriority = priorities[index],
                    shotQuality = qualities[index]
                };
            }
        }

        [BurstCompile]
        struct SortQueueJob : IJobParallelFor
        {
            public NativeArray<QueueEntry> priorityQueue;

            public void Execute(int index)
            {
                priorityQueue.Sort(new Comparer1());
            }

            struct Comparer1 : IComparer<QueueEntry>
            {
                public int Compare(QueueEntry x, QueueEntry y)
                {
                    int a = x.vcamPriority.channel - y.vcamPriority.channel;
                    int p = y.vcamPriority.priority - x.vcamPriority.priority; // high-to-low
                    float qf = y.shotQuality.value - x.shotQuality.value; // high-to-low
                    int q = math.select(-1, (int)math.ceil(qf), qf >= 0);
                    int s = y.vcamPriority.vcamSequence - x.vcamPriority.vcamSequence; // high-to-low
                    int e = y.entity.Index - x.entity.Index;    // high-to-low
                    int v = y.entity.Version - x.entity.Version; // high-to-low
                    return math.select(a,
                        math.select(p,
                            math.select(q,
                                math.select(s,
                                    math.select(e, v, e == 0),
                                    s == 0),
                                q == 0),
                            p == 0),
                        a == 0);
                }
            }
        }

        int m_vcamSequence = 1;
        public int NextVcamSequence { get { return m_vcamSequence++; } }

        public struct QueueEntry
        {
            public Entity entity;
            public CM_VcamPriority vcamPriority;
            public CM_VcamShotQuality shotQuality;
        }
        NativeArray<QueueEntry> m_priorityQueue;

        JobHandle QueueReadJobHandle = default(JobHandle);
        public JobHandle QueueWriteHandle { get; private set; }

        /// <summary>Get the vcam priority queue, which may not be written yet, for access by jobs.</summary>
        /// <param name="inputDeps">Adds a dependency on the jobs that write the queue</param>
        /// <returns>The queue.  Read-only</returns>
        public NativeArray<QueueEntry> GetPriorityQueueForJobs(ref JobHandle inputDeps)
        {
            inputDeps = JobHandle.CombineDependencies(inputDeps, QueueWriteHandle);
            return m_priorityQueue;
        }

        /// <summary>
        /// Register the jobs that are reading from the singleton priority queue, so that queue
        /// will not be prematurely corrupted.
        /// </summary>
        /// <param name="h">Jobs that are reading from the queue</param>
        /// <returns>the same h as passed in, for convenience</returns>
        public JobHandle RegisterPriorityQueueReadJobs(JobHandle h)
        {
            QueueReadJobHandle = JobHandle.CombineDependencies(QueueReadJobHandle, h);
            return h;
        }

        /// <summary>Get the vcam priority queue for immediate access.
        /// Waits for queue write jobs to complete.</summary>
        /// <returns>The queue</returns>
        public NativeArray<QueueEntry> GetPriorityQueueNow()
        {
            QueueWriteHandle.Complete();
            return m_priorityQueue;
        }

        public int GetChannelStartIndexInQueue(int channel)
        {
            QueueWriteHandle.Complete();
            int last = m_priorityQueue.Length;
            if (last == 0)
                return -1;

            // Most common case: it's the first one
            int value = m_priorityQueue[0].vcamPriority.channel;
            if (value == channel)
                return 0;
            if (value > channel)
                return -1;

            // Binary search
            int first = 0;
            int mid = first + ((last - first) >> 1);
            while (mid != first)
            {
                value = m_priorityQueue[mid].vcamPriority.channel;
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

            while (m_priorityQueue[first].vcamPriority.channel != channel)
                ++first;
            return first;
        }
    }
}
