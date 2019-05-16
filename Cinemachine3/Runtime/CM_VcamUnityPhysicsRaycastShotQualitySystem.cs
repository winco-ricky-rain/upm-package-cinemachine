using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using System.Runtime.CompilerServices;
using Unity.Cinemachine.Common;
using Unity.Physics;

namespace Unity.Cinemachine3
{
    [UnityEngine.ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamFinalizeSystem))]
    [UpdateBefore(typeof(CM_ChannelSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamUnityPhysicsRaycastShotQualitySystem : JobComponentSystem
    {
        EntityQuery m_vcamGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetEntityQuery(
                ComponentType.ReadOnly<CM_VcamChannel>(),
                ComponentType.ReadWrite<CM_VcamShotQuality>(),
                ComponentType.ReadOnly<CM_VcamPositionState>(),
                ComponentType.ReadOnly<CM_VcamRotationState>(),
                ComponentType.ReadOnly<CM_VcamLensState>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var channelSystem = World.GetOrCreateSystem<CM_ChannelSystem>();
            JobHandle jobDeps = channelSystem.InvokePerVcamChannel(
                m_vcamGroup, inputDeps,
                new QualityJobLaunch {
                    physicsWorldSystem = World.Active.GetOrCreateSystem<Physics.Systems.BuildPhysicsWorld>()
                });
            return jobDeps;
        }

        struct QualityJobLaunch : CM_ChannelSystem.IPerChannelJobLauncher
        {
            public Physics.Systems.BuildPhysicsWorld physicsWorldSystem;

            public JobHandle Execute(
                EntityQuery vcams, Entity channelEntity,
                CM_Channel c, CM_ChannelState state, JobHandle inputDeps)
            {
                var objectCount = vcams.CalculateLength();

                // These will be deallocated by the final job
                var raycastHits = new NativeArray<RaycastHit>(objectCount, Allocator.TempJob);

                var raycastsJob = new PerformRaycastsJob()
                {
                    collisionWorld = physicsWorldSystem.PhysicsWorld.CollisionWorld,
                    layerMask = -5, // GML todo: how to set this?
                    minDstanceFromTarget = 0, // GML todo: how to set this?
                    raycastHits = raycastHits
                };
                var raycastDeps = raycastsJob.Schedule(vcams, inputDeps);

                if (c.settings.IsOrthographic)
                {
                    var qualityJobOrtho = new CalculateQualityJobOrtho()
                    {
                        aspect = c.settings.aspect,
                        hits = raycastHits // deallocates on completion
                    };
                    return qualityJobOrtho.Schedule(vcams, raycastDeps);
                }
                var qualityJob = new CalculateQualityJob()
                {
                    aspect = c.settings.aspect,
                    hits = raycastHits // deallocates on completion
                };
                return qualityJob.Schedule(vcams, raycastDeps);
            }
        }

        [BurstCompile]
        struct PerformRaycastsJob : IJobForEachWithEntity<CM_VcamPositionState, CM_VcamRotationState>
        {
            [ReadOnly] public CollisionWorld collisionWorld;

            public int layerMask;
            public float minDstanceFromTarget;
            public NativeArray<RaycastHit> raycastHits;

            // GML todo: handle IgnoreTag or something like that ?

            public void Execute(
                Entity entity, int index,
                [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState)
            {
                // GML todo: check for no lookAt condition

                // cast back towards the camera to filter out target's collider
                float3 dir = posState.GetCorrected() - rotState.lookAtPoint;
                if (math.any(dir != 0))
                {
                    RaycastInput input = new RaycastInput()
                    {
                        Ray = new Ray { Origin = rotState.lookAtPoint, Direction = dir },
                        Filter = new CollisionFilter()
                        {
                            CategoryBits = ~0u, // all 1s, so all layers, collide with everything
                            MaskBits = ~0u,
                            GroupIndex = 0
                        }
                    };
                    RaycastHit hit = new RaycastHit();
                    collisionWorld.CastRay(input, out hit);
                    raycastHits[index] = hit;
                }
            }
        }

        [BurstCompile]
        struct CalculateQualityJob : IJobForEachWithEntity<
            CM_VcamShotQuality, CM_VcamPositionState, CM_VcamRotationState, CM_VcamLensState>
        {
            public float aspect;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastHit> hits;

            public void Execute(
                Entity entity, int index,
                ref CM_VcamShotQuality shotQuality, [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState, [ReadOnly] ref CM_VcamLensState lens)
            {
                float3 offset = rotState.lookAtPoint - posState.GetCorrected();
                offset = math.mul(math.inverse(rotState.GetCorrected()), offset); // camera-space
                bool isOnscreen = IsTargetOnscreen(offset, lens.fov, aspect);
                bool noObstruction = hits[index].SurfaceNormal.AlmostZero();
                bool isVisible = noObstruction && isOnscreen;
                shotQuality.value = math.select(0f, 1f, isVisible);
            }
        }

        [BurstCompile]
        struct CalculateQualityJobOrtho : IJobForEachWithEntity<
            CM_VcamShotQuality, CM_VcamPositionState, CM_VcamRotationState, CM_VcamLensState>
        {
            public float aspect;
            [ReadOnly] [DeallocateOnJobCompletion] public NativeArray<RaycastHit> hits;

            public void Execute(
                Entity entity, int index,
                ref CM_VcamShotQuality shotQuality, [ReadOnly] ref CM_VcamPositionState posState,
                [ReadOnly] ref CM_VcamRotationState rotState, [ReadOnly] ref CM_VcamLensState lens)
            {
                float3 offset = rotState.lookAtPoint - posState.GetCorrected();
                offset = math.mul(math.inverse(rotState.GetCorrected()), offset); // camera-space
                bool isOnscreen = IsTargetOnscreenOrtho(offset, lens.fov, aspect);
                bool noObstruction = hits[index].SurfaceNormal.AlmostZero();
                bool isVisible = noObstruction && isOnscreen;
                shotQuality.value = math.select(0f, 1f, isVisible);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsTargetOnscreen(float3 dir, float size, float aspect)
        {
            float fovY = 0.5f * math.radians(size);    // size is fovH in deg.  need half-fov in rad
            float2 fov = new float2(math.atan(math.tan(fovY) * aspect), fovY);
            float2 angle = new float2(
                MathHelpers.AngleUnit(
                    math.normalize(dir.ProjectOntoPlane(math.up())), new float3(0, 0, 1)),
                MathHelpers.AngleUnit(
                    math.normalize(dir.ProjectOntoPlane(new float3(1, 0, 0))), new float3(0, 0, 1)));
            return math.all(angle <= fov);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsTargetOnscreenOrtho(float3 dir, float size, float aspect)
        {
            float2 s = new float2(size * aspect, size);
            return math.all(math.abs(new float2(dir.x, dir.y)) < s);
        }
    }
}
