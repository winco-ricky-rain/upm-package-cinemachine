using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Transforms;

namespace Cinemachine.ECS
{
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    public class CM_VcamPushToTransformSystem : JobComponentSystem
    {
        ComponentGroup m_mainGroup;

        protected override void OnCreateManager()
        {
            m_mainGroup = GetComponentGroup(
                ComponentType.Create<Position>(), 
                ComponentType.Create<Rotation>(), 
                ComponentType.ReadOnly<CM_VcamPosition>(), 
                ComponentType.ReadOnly<CM_VcamRotation>());
        }

        [BurstCompile]
        struct PushToTransformJob : IJobParallelFor
        {
            public ComponentDataArray<Position> positions;
            public ComponentDataArray<Rotation> rotations;
            [ReadOnly] public ComponentDataArray<CM_VcamPosition> vcamPositions;
            [ReadOnly] public ComponentDataArray<CM_VcamRotation> vcamRotations;

            public void Execute(int index)
            {
                positions[index] = new Position { Value = vcamPositions[index].raw };
                rotations[index] = new Rotation { Value = vcamRotations[index].raw };
            }
        }
        
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new PushToTransformJob
            {
                positions = m_mainGroup.GetComponentDataArray<Position>(),
                rotations = m_mainGroup.GetComponentDataArray<Rotation>(),
                vcamPositions = m_mainGroup.GetComponentDataArray<CM_VcamPosition>(),
                vcamRotations = m_mainGroup.GetComponentDataArray<CM_VcamRotation>()
            };
            var h = job.Schedule(m_mainGroup.CalculateLength(), 32, inputDeps);
            //World.GetExistingManager<TransformSystem>().Update(); // GML hack
            return h;
        }
    }
}
