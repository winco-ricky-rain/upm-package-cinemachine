using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Burst;
using Unity.Mathematics;
using System;
using UnityEngine;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_Target : IComponentData
    {
        public float radius;
    }

    [Serializable]
    [InternalBufferCapacity(4)]
    public struct CM_GroupBufferElement : IBufferElementData
    {
        public Entity target;
        public float weight;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(EndFrameTransformSystem))]
    public class CM_TargetSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;
        ComponentGroup m_groupGroup;

        public struct TargetInfo
        {
            public float3 position;
            public float radius;
            public quaternion rotation;
        }
        NativeHashMap<Entity, TargetInfo> m_targetLookup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Target>(),
                ComponentType.ReadOnly<LocalToWorld>());

            m_groupGroup = GetComponentGroup(
                ComponentType.Create<CM_Target>(),
                ComponentType.ReadOnly(typeof(CM_GroupBufferElement)));

            m_targetLookup = new NativeHashMap<Entity, TargetInfo>(64, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            m_targetLookup.Dispose();
            base.OnDestroyManager();
        }

        [BurstCompile]
        struct HashTargets : IJobParallelFor
        {
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public ComponentDataArray<CM_Target> targets;
            [ReadOnly] public ComponentDataArray<LocalToWorld> positions;
            public NativeHashMap<Entity, TargetInfo>.Concurrent hashMap;

            public void Execute(int index)
            {
                hashMap.TryAdd(entities[index], new TargetInfo()
                {
                    position = math.transform(positions[index].Value, float3.zero),
                    rotation = new quaternion(positions[index].Value),
                    radius = targets[index].radius
                });
            }
        }

        [BurstCompile]
        struct UpdateGroups : IJobParallelFor
        {
            [ReadOnly] public BufferArray<CM_GroupBufferElement> groupBuffers;
            [ReadOnly] public NativeHashMap<Entity, TargetInfo> hashMap;
            public NativeArray<TargetInfo> infoArray;

            public void Execute(int index)
            {
                var buffer = groupBuffers[index];

                int numTargets = 0;
                float3 avgPos = float3.zero;
                float avgWeight = 0;
                for (int i = 0; i < buffer.Length; ++i)
                {
                    var b = buffer[i];
                    if (hashMap.TryGetValue(b.target, out TargetInfo item))
                    {
                        ++numTargets;
                        avgPos += item.position * b.weight;
                        avgWeight += b.weight;
                    }
                }

                // This is a very approximate implementation
                if (numTargets > 0 && avgWeight > 0.001f)
                {
                    avgPos /= avgWeight;
                    avgWeight /= numTargets;
                    numTargets = 0;
                    float3 minPos = float3.zero;
                    float3 maxPos = float3.zero;
                    for (int i = 0; i < buffer.Length; ++i)
                    {
                        var b = buffer[i];
                        if (hashMap.TryGetValue(b.target, out TargetInfo item))
                        {
                            float w = math.max(1, b.weight / avgWeight);
                            float3 p = math.lerp(avgPos, item.position, w);
                            float3 r = math.lerp(0, item.radius, w) * new float3(1, 1, 1);
                            float3 p0 = p - r;
                            float3 p1 = p + r;
                            minPos = math.select(math.min(minPos, p0), p0, numTargets == 0);
                            maxPos = math.select(math.min(maxPos, p1), p1, numTargets == 0);
                            ++numTargets;
                        }
                        infoArray[index] = new TargetInfo
                        {
                            position = (minPos + maxPos) / 2,
                            radius = math.length(maxPos - minPos) / 2,
                            rotation = quaternion.identity
                        };
                    }
                }
            }
        }

        [BurstCompile]
        struct SetGroupInfo : IJobParallelFor
        {
            [ReadOnly] public EntityArray entities;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<TargetInfo> infoArray;
            public NativeHashMap<Entity, TargetInfo>.Concurrent hashMap;
            public ComponentDataArray<CM_Target> targets;

            public void Execute(int index)
            {
                hashMap.TryAdd(entities[index], infoArray[index]);
                targets[index] = new CM_Target { radius = infoArray[index].radius };
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Make sure all readers have finished with the table
            TargetTableReadJobHandle.Complete();
            TargetTableReadJobHandle = default(JobHandle);

            var objectCount = m_mainGroup.CalculateLength();
            var groupCount = m_groupGroup == null ? 0 : m_groupGroup.CalculateLength();
            m_targetLookup.Clear();
            m_targetLookup.Capacity = math.max(m_targetLookup.Capacity, objectCount + groupCount);

            var hashJob = new HashTargets()
            {
                entities = m_mainGroup.GetEntityArray(),
                targets = m_mainGroup.GetComponentDataArray<CM_Target>(),
                positions = m_mainGroup.GetComponentDataArray<LocalToWorld>(),
                hashMap = m_targetLookup.ToConcurrent()
            };
            TargetTableWriteHandle = hashJob.Schedule(objectCount, 32, inputDeps);

            if (groupCount > 0)
            {
                var infoArray = new NativeArray<TargetInfo>(groupCount, Allocator.TempJob);
                var groupJob = new UpdateGroups
                {
                    groupBuffers = m_groupGroup.GetBufferArray<CM_GroupBufferElement>(),
                    hashMap = m_targetLookup,
                    infoArray = infoArray
                };
                TargetTableWriteHandle = groupJob.Schedule(groupCount, 32, TargetTableWriteHandle);

                var setGroupsJob = new SetGroupInfo
                {
                    entities = m_groupGroup.GetEntityArray(),
                    infoArray = infoArray,
                    hashMap = m_targetLookup.ToConcurrent(),
                    targets = m_groupGroup.GetComponentDataArray<CM_Target>()
                };
                TargetTableWriteHandle = setGroupsJob.Schedule(groupCount, 32, TargetTableWriteHandle);
            }
            return TargetTableWriteHandle;
        }

        JobHandle TargetTableReadJobHandle = default(JobHandle);
        public JobHandle TargetTableWriteHandle { get; private set; }

        /// <summary>
        /// Get the singleton TargetLookup table, which may not be written yet, for access by jobs.
        /// This table converts an Entity to CM_TargetLookup.TargetInfo.
        /// </summary>
        /// <param name="inputDeps">Adds a dependency on the jobs that write the table</param>
        /// <returns>The lookup table.  Read-only</returns>
        public NativeHashMap<Entity, TargetInfo> GetTargetLookupForJobs(ref JobHandle inputDeps)
        {
            inputDeps = JobHandle.CombineDependencies(inputDeps, TargetTableWriteHandle);
            return m_targetLookup;
        }

        /// <summary>
        /// Get the singleton TargetLookup table for immediate access.
        /// Waits for table write jobs to complete.
        /// This table converts an Entity to CM_TargetLookup.TargetInfo.
        /// </summary>
        /// <returns>The lookup table.</returns>
        public NativeHashMap<Entity, TargetInfo> GetTargetLookupNow()
        {
            TargetTableWriteHandle.Complete();
            return m_targetLookup;
        }

        /// <summary>
        /// Register the jobs that are reading from the singleton target lookup table, so that table
        /// will not be prematurely corrupted.
        /// </summary>
        /// <param name="h">Jobs that are reading from the table</param>
        /// <returns>the same h as passed in, for convenience</returns>
        public JobHandle RegisterTargetLookupReadJobs(JobHandle h)
        {
            TargetTableReadJobHandle = JobHandle.CombineDependencies(TargetTableReadJobHandle, h);
            return h;
        }
    }
}
