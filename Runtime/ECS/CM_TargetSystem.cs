using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Burst;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    public struct CM_TargetLookup : ISystemStateSharedComponentData
    {
        public struct TargetInfo
        {
            public float3 position;
            public float radius;
            public quaternion rotation;
        }
        public NativeHashMap<Entity, TargetInfo> targetLookup;
    }
    
    public class CM_TargetSystem : JobComponentSystem
    {
        Entity m_systemSingleton;
        ComponentGroup m_mainGroup;
        ComponentGroup m_targetGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Target>(), 
                ComponentType.ReadOnly<LocalToWorld>());

            m_targetGroup = GetComponentGroup(
                ComponentType.Create<CM_TargetLookup>());
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            m_systemSingleton = EntityManager.CreateEntity(typeof(CM_TargetLookup));
            EntityManager.SetSharedComponentData(m_systemSingleton, new CM_TargetLookup
            { 
                targetLookup = new NativeHashMap<Entity, CM_TargetLookup.TargetInfo>(64, Allocator.Persistent) 
            });
        }

        protected override void OnStopRunning()
        {
            m_targetGroup.GetSharedComponentDataArray<CM_TargetLookup>()[0].targetLookup.Dispose();
            EntityManager.DestroyEntity(m_systemSingleton);
            base.OnStopRunning();
        }

        [BurstCompile]
        struct HashTargets : IJobParallelFor
        {
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public ComponentDataArray<CM_Target> targets;
            [ReadOnly] public ComponentDataArray<LocalToWorld> positions;
            public NativeHashMap<Entity, CM_TargetLookup.TargetInfo>.Concurrent hashMap;

            public void Execute(int index)
            {
                hashMap.TryAdd(entities[index], new CM_TargetLookup.TargetInfo() 
                { 
                    position = math.transform(positions[index].Value, float3.zero),
                    rotation = math.mul(positions[index].Value, quaternion.identity.value),
                    radius = targets[index].radius
                });
            }
        }
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            TargetTableReadJobHandle.Complete();
            TargetTableReadJobHandle = default(JobHandle);
            var targetLookup = m_targetGroup.GetSharedComponentDataArray<CM_TargetLookup>()[0];
            targetLookup.targetLookup.Clear();
            targetLookup.targetLookup.Capacity = m_mainGroup.GetComponentDataArray<CM_Target>().Length;

            var hashJob = new HashTargets()
            {
                entities = m_mainGroup.GetEntityArray(),
                targets = m_mainGroup.GetComponentDataArray<CM_Target>(),
                positions = m_mainGroup.GetComponentDataArray<LocalToWorld>(),
                hashMap = targetLookup.targetLookup.ToConcurrent()
            };
            TargetTableWriteHandle = hashJob.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps);
            return TargetTableWriteHandle;
        } 

        JobHandle TargetTableReadJobHandle = default(JobHandle);
        public JobHandle TargetTableWriteHandle { get; private set; }
        
        /// <summary>
        /// Get the singleton TargetLookup table.  This table converts an Entity to CM_TargetLookup.TargetInfo.
        /// </summary>
        /// <param name="inputDeps">Adds a dependency on the jobs that write the table</param>
        /// <returns>The lokup table.  Don't use it if it's not created</returns>
        public NativeHashMap<Entity, CM_TargetLookup.TargetInfo> GetTargetLookup(ref JobHandle inputDeps)
        {
            var targetLookupArray = m_targetGroup.GetSharedComponentDataArray<CM_TargetLookup>();
            if (targetLookupArray.Length == 0)
                return new NativeHashMap<Entity, CM_TargetLookup.TargetInfo>();
            inputDeps = JobHandle.CombineDependencies(inputDeps, TargetTableWriteHandle);
            return targetLookupArray[0].targetLookup;
        }

        /// <summary>
        /// Register the jobs that are reading from the singleton target lookup table, so that table
        /// will not be prematurely corrupted.
        /// </summary>
        /// <param name="h">Jobs that are reading from the table</param>
        /// <returns>the same h as passed in, for convenience</returns>
        public JobHandle RegisterTargetTableReadJobs(JobHandle h)
        {
            TargetTableReadJobHandle = JobHandle.CombineDependencies(TargetTableReadJobHandle, h);
            return h;
        }
    }
}
