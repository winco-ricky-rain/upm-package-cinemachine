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
        public float3 offset;
        public float radius;
    }

    [Serializable]
    [InternalBufferCapacity(4)]
    public struct CM_GroupBufferElement : IBufferElementData
    {
        public Entity target;
        public float weight;
    }

    // Dummy component for iterating groups
    internal struct CM_Group : IComponentData {}

    [ExecuteAlways]
    //[UpdateAfter(typeof(EndFrameWorldToLocalSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_TargetSystem : JobComponentSystem
    {
        EntityQuery m_mainGroup;
        EntityQuery m_groupGroup;
        EntityQuery m_missingGroupGroup;

        public struct TargetInfo
        {
            public float3 position;
            public float radius;
            public quaternion rotation;
        }
        NativeHashMap<Entity, TargetInfo> m_targetLookup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetEntityQuery(
                ComponentType.ReadOnly<CM_Target>(),
                ComponentType.ReadOnly<LocalToWorld>(),
                ComponentType.Exclude<CM_Group>());

            m_groupGroup = GetEntityQuery(
                ComponentType.ReadWrite<CM_Target>(),
                ComponentType.ReadWrite<CM_Group>(),
                ComponentType.ReadOnly(typeof(CM_GroupBufferElement)));

            m_missingGroupGroup = GetEntityQuery(
                ComponentType.ReadOnly(typeof(CM_GroupBufferElement)),
                ComponentType.Exclude<CM_Group>());

            m_targetLookup = new NativeHashMap<Entity, TargetInfo>(64, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            m_targetLookup.Dispose();
            base.OnDestroyManager();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing group components
            EntityManager.AddComponent(m_missingGroupGroup,
                ComponentType.ReadWrite<CM_Group>());

            // Make sure all readers have finished with the table
            TargetTableReadJobHandle.Complete();
            TargetTableReadJobHandle = default(JobHandle);

            var objectCount = m_mainGroup.CalculateLength();
            var groupCount = m_groupGroup == null ? 0 : m_groupGroup.CalculateLength();
            m_targetLookup.Clear();
            m_targetLookup.Capacity = math.max(m_targetLookup.Capacity, objectCount + groupCount);

            var hashJob = new HashTargets()
            {
                hashMap = m_targetLookup.ToConcurrent()
            };
            TargetTableWriteHandle = hashJob.Schedule(m_mainGroup, inputDeps);

            if (groupCount > 0)
            {
                var infoArray = new NativeArray<TargetInfo>(groupCount, Allocator.TempJob);
                var groupJob = new UpdateGroups
                {
                    groupBuffers = GetBufferFromEntity<CM_GroupBufferElement>(),
                    hashMap = m_targetLookup,
                    infoArray = infoArray
                };
                TargetTableWriteHandle = groupJob.Schedule(m_groupGroup, TargetTableWriteHandle);

                var setGroupsJob = new SetGroupInfo
                {
                    infoArray = infoArray,
                    hashMap = m_targetLookup.ToConcurrent()
                };
                TargetTableWriteHandle = setGroupsJob.Schedule(m_groupGroup, TargetTableWriteHandle);
            }
            return TargetTableWriteHandle;
        }

        [BurstCompile]
        struct HashTargets : IJobForEachWithEntity<LocalToWorld, CM_Target>
        {
            public NativeHashMap<Entity, TargetInfo>.Concurrent hashMap;

            public void Execute(
                Entity entity, int index,
                [ReadOnly] ref LocalToWorld pos,
                [ReadOnly] ref CM_Target t)
            {
                var rot = pos.Value.GetRotationFromTRS();
                hashMap.TryAdd(entity, new TargetInfo()
                {
                    rotation = rot,
                    position = pos.Value.GetTranslationFromTRS() + math.mul(rot, t.offset),
                    radius = t.radius,
                });
            }
        }

        [BurstCompile]
        struct UpdateGroups : IJobForEachWithEntity<CM_Group>
        {
            [ReadOnly] public BufferFromEntity<CM_GroupBufferElement> groupBuffers;
            [ReadOnly] public NativeHashMap<Entity, TargetInfo> hashMap;
            public NativeArray<TargetInfo> infoArray;

            public void Execute(Entity entity, int index, [ReadOnly] ref CM_Group groupDummy)
            {
                var buffer = groupBuffers[entity];

                float3 avgPos = float3.zero;
                float weightSum = 0;
                float maxWeight = 0;
                for (int i = 0; i < buffer.Length; ++i)
                {
                    var b = buffer[i];
                    if (hashMap.TryGetValue(b.target, out TargetInfo item))
                    {
                        avgPos += item.position * b.weight;
                        weightSum += b.weight;
                        maxWeight = math.max(maxWeight, b.weight);
                    }
                }

                // This is a very approximate implementation
                if (maxWeight > MathHelpers.Epsilon)
                {
                    avgPos /= weightSum;
                    bool gotOne = false;
                    float3 minPos = float3.zero;
                    float3 maxPos = float3.zero;
                    for (int i = 0; i < buffer.Length; ++i)
                    {
                        var b = buffer[i];
                        if (hashMap.TryGetValue(b.target, out TargetInfo item))
                        {
                            float w = b.weight / maxWeight;
                            float3 p = math.lerp(avgPos, item.position, w);
                            float3 r = math.lerp(0, item.radius, w) * new float3(1, 1, 1);
                            float3 p0 = p - r;
                            float3 p1 = p + r;
                            minPos = math.select(p0, math.min(minPos, p0), gotOne);
                            maxPos = math.select(p1, math.max(maxPos, p1), gotOne);
                            gotOne = true;
                        }
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

        [BurstCompile]
        struct SetGroupInfo : IJobForEachWithEntity<CM_Target>
        {
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<TargetInfo> infoArray;
            public NativeHashMap<Entity, TargetInfo>.Concurrent hashMap;

            public void Execute(Entity entity, int index, ref CM_Target t)
            {
                var info = infoArray[index];
                hashMap.TryAdd(entity, info);
                t.radius = info.radius;
            }
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
