//using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
//using UnityEngine;

namespace Cinemachine.ECS
{
#if false
    [Serializable]
    public struct CM_SharedTarget : ISystemStateSharedComponentData
    {
        // GML TODO: Maybe put the target lookup in here, on a dynamically-created entity
    }
#endif

    public class CM_TargetSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        public struct TargetInfo
        {
            public float3 position;
            public quaternion rotation;
            public float radius;
        }

        /// Read-only please
        internal NativeHashMap<Entity, TargetInfo> TargetLookup { get; private set; }

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.ReadOnly(typeof(CM_Target)), 
                ComponentType.ReadOnly(typeof(Position)), 
                ComponentType.ReadOnly(typeof(Rotation)));

            TargetLookup = new NativeHashMap<Entity, TargetInfo>(64, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            TargetLookup.Dispose();
            base.OnDestroyManager();
        }

        [BurstCompile]
        struct HashTargets : IJobParallelFor
        {
            [ReadOnly] public EntityArray entities;
            [ReadOnly] public ComponentDataArray<CM_Target> targets;
            [ReadOnly] public ComponentDataArray<Position> positions;
            [ReadOnly] public ComponentDataArray<Rotation> rotations;
            public NativeHashMap<Entity, TargetInfo>.Concurrent hashMap;

            public void Execute(int index)
            {
                hashMap.TryAdd(entities[index], new TargetInfo() 
                { 
                    position = positions[index].Value,
                    rotation = rotations[index].Value,
                    radius = targets[index].radius
                });
            }
        }
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            TargetLookup.Clear();
            var job = new HashTargets()
            {
                entities = m_mainGroup.GetEntityArray(),
                targets = m_mainGroup.GetComponentDataArray<CM_Target>(),
                positions = m_mainGroup.GetComponentDataArray<Position>(),
                rotations = m_mainGroup.GetComponentDataArray<Rotation>(),
                hashMap = TargetLookup.ToConcurrent()
            };
            return job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps);
        } 
    }
}
