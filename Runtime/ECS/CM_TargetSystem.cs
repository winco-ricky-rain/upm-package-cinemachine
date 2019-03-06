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
        public float3 warpDelta; // GML todo: make use of this
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
        ComponentGroup m_mainGroup;
        ComponentGroup m_groupGroup;
        ComponentGroup m_missingGroupGroup;

        EndSimulationEntityCommandBufferSystem m_missingStateBarrier;

        public struct TargetInfo
        {
            public float3 position;
            public float radius;
            public quaternion rotation;
            public float3 warpDelta;
        }
        NativeHashMap<Entity, TargetInfo> m_targetLookup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Target>(),
                ComponentType.ReadOnly<LocalToWorld>());

            m_groupGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Target>(),
                ComponentType.ReadOnly(typeof(CM_GroupBufferElement)));

            m_missingGroupGroup = GetComponentGroup(
                ComponentType.ReadOnly(typeof(CM_GroupBufferElement)),
                ComponentType.Exclude<CM_Group>());

            m_targetLookup = new NativeHashMap<Entity, TargetInfo>(64, Allocator.Persistent);
            m_missingStateBarrier = World.GetOrCreateManager<EndSimulationEntityCommandBufferSystem>();
        }

        protected override void OnDestroyManager()
        {
            m_targetLookup.Dispose();
            base.OnDestroyManager();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing group components
            var missingGroupEntities = m_missingGroupGroup.ToEntityArray(Allocator.TempJob);
            if (missingGroupEntities.Length > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                for (int i = 0; i < missingGroupEntities.Length; ++i)
                    cb.AddComponent(missingGroupEntities[i], new CM_Group());
            }
            missingGroupEntities.Dispose();

            // Make sure all readers have finished with the table
            TargetTableReadJobHandle.Complete();
            TargetTableReadJobHandle = default;

            var objectCount = m_mainGroup.CalculateLength();
            var groupCount = m_groupGroup == null ? 0 : m_groupGroup.CalculateLength();
            m_targetLookup.Clear();
            m_targetLookup.Capacity = math.max(m_targetLookup.Capacity, objectCount + groupCount);

            var hashJob = new HashTargets()
            {
                hashMap = m_targetLookup.ToConcurrent()
            };
            TargetTableWriteHandle = hashJob.ScheduleGroup(m_mainGroup, inputDeps);

            if (groupCount > 0)
            {
                var infoArray = new NativeArray<TargetInfo>(groupCount, Allocator.TempJob);
                var groupJob = new UpdateGroups
                {
                    groupBuffers = GetBufferFromEntity<CM_GroupBufferElement>(),
                    hashMap = m_targetLookup,
                    infoArray = infoArray
                };
                TargetTableWriteHandle = groupJob.ScheduleGroup(m_groupGroup, TargetTableWriteHandle);

                var setGroupsJob = new SetGroupInfo
                {
                    infoArray = infoArray,
                    hashMap = m_targetLookup.ToConcurrent(),
                };
                TargetTableWriteHandle = setGroupsJob.ScheduleGroup(m_mainGroup, TargetTableWriteHandle);
            }
            return TargetTableWriteHandle;
        }

        [BurstCompile]
        struct HashTargets : IJobProcessComponentDataWithEntity<LocalToWorld, CM_Target>
        {
            public NativeHashMap<Entity, TargetInfo>.Concurrent hashMap;

            public void Execute(
                Entity entity, int index,
                [ReadOnly] ref LocalToWorld pos,
                [ReadOnly] ref CM_Target t)
            {
                var rot = pos.Value.GetRotation();
                hashMap.TryAdd(entity, new TargetInfo()
                {
                    rotation = rot,
                    position = pos.Value.GetTranslation() + math.mul(rot, t.offset),
                    radius = t.radius,
                    warpDelta = t.warpDelta
                });
                t.warpDelta = float3.zero;
            }
        }

        [BurstCompile]
        struct UpdateGroups : IJobProcessComponentDataWithEntity<CM_Group>
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
                            float w = math.max(1, b.weight / maxWeight);
                            float3 p = math.lerp(avgPos, item.position, w);
                            float3 r = math.lerp(0, item.radius, w) * new float3(1, 1, 1);
                            float3 p0 = p - r;
                            float3 p1 = p + r;
                            minPos = math.select(p0, math.min(minPos, p0), gotOne);
                            maxPos = math.select(p1, math.min(maxPos, p1), gotOne);
                            gotOne = true;
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
        struct SetGroupInfo : IJobProcessComponentDataWithEntity<CM_Target>
        {
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<TargetInfo> infoArray;
            public NativeHashMap<Entity, TargetInfo>.Concurrent hashMap;

            public void Execute(Entity entity, int index, ref CM_Target t)
            {
                hashMap.TryAdd(entity, infoArray[index]);
                t.radius = infoArray[index].radius;
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
