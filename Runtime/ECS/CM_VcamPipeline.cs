using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamLookAtTarget : IComponentData
    {
        public Entity target;
    }

    [Serializable]
    public struct CM_VcamFollowTarget : IComponentData
    {
        public Entity target;
    }
}

#if false // notes
    //[UpdateBefore(typeof(System...)]
    public class SpawnRandomInSphereSystem : JobComponentSystem
    {
        struct SpawnRandomInSphereInstance
        {
            public int spawnerIndex;
            public Entity sourceEntity;
            public float3 position;
#pragma warning disable 649
            public float radius;
#pragma warning restore 649
        }

        ComponentGroup m_MainGroup;

        protected override void OnCreateManager()
        {
            m_MainGroup = GetComponentGroup(ComponentType.ReadOnly(typeof(SpawnRandomInSphere)), typeof(Position));
        }

        protected override void OnUpdate()
        {
            m_MainGroup.GetComponentDataArray<SpawnRandomInSphere>
            var uniqueTypes = new List<SpawnRandomInSphere>(10);
                var entities = m_MainGroup.GetEntityArray();
                spawnInstanceCount += entities.Length;
            }

            if (spawnInstanceCount == 0)
                return;

            var spawnInstances = new NativeArray<SpawnRandomInSphereInstance>(spawnInstanceCount, Allocator.Temp);
            {
                int spawnIndex = 0;
                for (int sharedIndex = 0; sharedIndex != uniqueTypes.Count; sharedIndex++)
                {
                    var spawner = uniqueTypes[sharedIndex];
                    m_MainGroup.SetFilter(spawner);
                    var entities = m_MainGroup.GetEntityArray();
                    var positions = m_MainGroup.GetComponentDataArray<Position>();

                    for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
                    {
                        var spawnInstance = new SpawnRandomInSphereInstance();

                        spawnInstance.sourceEntity = entities[entityIndex];
                        spawnInstance.spawnerIndex = sharedIndex;
                        spawnInstance.position = positions[entityIndex].Value;

                        spawnInstances[spawnIndex] = spawnInstance;
                        spawnIndex++;
                    }
                }
            }

            for (int spawnIndex = 0; spawnIndex < spawnInstances.Length; spawnIndex++)
            {
                int spawnerIndex = spawnInstances[spawnIndex].spawnerIndex;
                var spawner = uniqueTypes[spawnerIndex];
                int count = spawner.count;
                var entities = new NativeArray<Entity>(count,Allocator.Temp);
                var prefab = spawner.prefab;
                float radius = spawner.radius;
                var spawnPositions = new NativeArray<float3>(count, Allocator.Temp);
                float3 center = spawnInstances[spawnIndex].position;
                var sourceEntity = spawnInstances[spawnIndex].sourceEntity;

                GeneratePoints.RandomPointsInSphere(center,radius,ref spawnPositions);

                EntityManager.Instantiate(prefab, entities);

                for (int i = 0; i < count; i++)
                {
                    var position = new Position
                    {
                        Value = spawnPositions[i]
                    };
                    EntityManager.SetComponentData(entities[i],position);
                }

                EntityManager.RemoveComponent<SpawnRandomInSphere>(sourceEntity);

                spawnPositions.Dispose();
                entities.Dispose();
            }
            spawnInstances.Dispose();
        }
    }
}

#endif
