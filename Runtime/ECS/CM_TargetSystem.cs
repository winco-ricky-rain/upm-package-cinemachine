using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Burst;
using Unity.Mathematics;
using System;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_Target : IComponentData
    {
        public float radius;
    }

    [UpdateAfter(typeof(EndFrameTransformSystem))]
    public class CM_TargetSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

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
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Make sure all readers have finished with the table
            TargetTableReadJobHandle.Complete();
            TargetTableReadJobHandle = default(JobHandle);

            var objectCount = m_mainGroup.CalculateLength();
            m_targetLookup.Clear();
            m_targetLookup.Capacity = math.max(m_targetLookup.Capacity, objectCount);

            var hashJob = new HashTargets()
            {
                entities = m_mainGroup.GetEntityArray(),
                targets = m_mainGroup.GetComponentDataArray<CM_Target>(),
                positions = m_mainGroup.GetComponentDataArray<LocalToWorld>(),
                hashMap = m_targetLookup.ToConcurrent()
            };
            TargetTableWriteHandle = hashJob.Schedule(objectCount, 32, inputDeps);
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

    // These systems define the CM Vcam pipeline, in this order.  
    // Use them to ensure correct ordering of CM pipeline systems

    [UpdateAfter(typeof(CM_TargetSystem))]
    public class CM_VcamBodySystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [UpdateAfter(typeof(CM_VcamBodySystem))]
    public class CM_VcamAimSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [UpdateAfter(typeof(CM_VcamAimSystem))]
    public class CM_VcamCorrectionSystem : ComponentSystem
    {
        protected override void OnUpdate() {} // Do nothing
    }

    [UpdateAfter(typeof(CM_VcamCorrectionSystem))]
    public class CM_VcamFinalizeSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(ComponentType.Create<CM_VcamPosition>());
        }

        [BurstCompile]
        struct FinalizeJob : IJobParallelFor
        {
            public ComponentDataArray<CM_VcamPosition> positions;

            public void Execute(int index)
            {
                positions[index] = new CM_VcamPosition
                {
                    raw = positions[index].raw,
                    dampingBypass = float3.zero,
                    up = positions[index].up,
                    previousFrameDataIsValid = 1
                };
            }
        }
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new FinalizeJob
            {
                positions = m_mainGroup.GetComponentDataArray<CM_VcamPosition>(),
            };
            return job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps);
        }
    }
}
