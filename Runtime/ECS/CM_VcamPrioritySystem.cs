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
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamPriority>());

            m_vcamSequence = 1;
            m_priorityQueue = new NativeArray<QueueEntry>(16, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            m_priorityQueue.Dispose();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var length = m_mainGroup.CalculateLength();
            if (m_priorityQueue.Length != length)
            {
                m_priorityQueue.Dispose();
                m_priorityQueue = new NativeArray<QueueEntry>(length, Allocator.Persistent);
            }

            var populateDeps = new PopulateQueueJob
            {
                entities = m_mainGroup.GetEntityArray(),
                priorities = m_mainGroup.GetComponentDataArray<CM_VcamPriority>(),
                qualities = GetComponentDataFromEntity<CM_VcamShotQuality>(true),
                priorityQueue = m_priorityQueue
            }.Schedule(length, 32, inputDeps);

            QueueWriteHandle = new SortQueueJob
                { priorityQueue = m_priorityQueue }.Schedule(1, 1, populateDeps);
            return QueueWriteHandle;
        }

        [BurstCompile]
        struct PopulateQueueJob : IJobParallelFor
        {
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public ComponentDataArray<CM_VcamPriority> priorities;
            [ReadOnly] public ComponentDataFromEntity<CM_VcamShotQuality> qualities;
            public NativeArray<QueueEntry> priorityQueue;

            public void Execute(int index)
            {
                var entity = entities[index];
                if (qualities.Exists(entity))
                {
                    priorityQueue[index] = new QueueEntry
                    {
                        entity = entity,
                        vcamPriority = priorities[index],
                        shotQuality = qualities[entity]
                    };
                }
                else
                {
                    priorityQueue[index] = new QueueEntry
                    {
                        entity = entity,
                        vcamPriority = priorities[index],
                        shotQuality = new CM_VcamShotQuality { value = CM_VcamShotQuality.DefaultValue }
                    };
                }
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

        public JobHandle QueueWriteHandle { get; private set; }

        /// <summary>Get the vcam priority queue, which may not be written yet, for access by jobs.</summary>
        /// <param name="inputDeps">Adds a dependency on the jobs that write the queue</param>
        /// <returns>The queue.  Read-only</returns>
        public NativeArray<QueueEntry> GetPriorityQueueForJobs(ref JobHandle inputDeps)
        {
            inputDeps = JobHandle.CombineDependencies(inputDeps, QueueWriteHandle);
            return m_priorityQueue;
        }

        /// <summary>Get the vcam priority queue for immediate access.
        /// Waits for queue write jobs to complete.</summary>
        /// <returns>The queue</returns>
        public NativeArray<QueueEntry> GetPriorityQueueNow(bool waitForWriteComplete)
        {
            if (waitForWriteComplete)
                QueueWriteHandle.Complete();
            return m_priorityQueue;
        }
    }
}
