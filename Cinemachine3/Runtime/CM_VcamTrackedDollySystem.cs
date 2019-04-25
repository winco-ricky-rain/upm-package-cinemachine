using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System.Runtime.CompilerServices;
using UnityEngine;
using System;
using Unity.Transforms;

namespace Unity.Cinemachine3
{
    [Serializable]
    public struct CM_VcamTrackedDolly : IComponentData
    {
        /// <summary>The path to which the camera will be constrained.  This must be non-null.</summary>
        [Tooltip("The path to which the camera will be constrained.  This must be non-null.")]
        public Entity path;

        /// <summary>The position along the path at which the camera will be placed.
        /// This can be animated directly, or set automatically by the Auto-Dolly feature
        /// to get as close as possible to the Follow target.</summary>
        [Tooltip("The position along the path at which the camera will be placed.  "
            + "This can be animated directly, or set automatically by the Auto-Dolly feature "
            + "to get as close as possible to the Follow target.  The value is interpreted "
            + "according to the Position Units setting.")]
        public float pathPosition;

        /// <summary>How to interpret the Path Position</summary>
        [Tooltip("How to interpret Path Position.  If set to Path Units, values are as follows: "
            + "0 represents the first waypoint on the path, 1 is the second, and so on.  "
            + "Values in-between are points on the path in between the waypoints.  "
            + "If set to Distance, then Path Position represents distance along the path.")]
        public CM_PathSystem.PositionUnits positionUnits;

        /// <summary>Where to put the camera realtive to the path postion.
        /// X is perpendicular to the path, Y is up, and Z is parallel to the path.</summary>
        [Tooltip("Where to put the camera relative to the path position.  "
        + " X is perpendicular to the path, Y is up, and Z is parallel to the path.  "
        + "This allows the camera to be offset from the path itself (as if on a tripod, for example).")]
        public float3 pathOffset;

        /// <summary>How aggressively the camera tries to maintain the offset perpendicular to the path.
        /// Small numbers are more responsive, rapidly translating the camera to keep the target's
        /// x-axis offset.  Larger numbers give a more heavy slowly responding camera.
        /// Using different settings per axis can yield a wide range of camera behaviors</summary>
        [Tooltip("How aggressively the camera tries to maintain its position relative to the path.  "
            + "Small numbers are more responsive, rapidly translating "
            + "the camera to keep the path offset.  Larger numbers give a more heavy "
            + "slowly responding camera. Using different settings per axis can yield a wide range "
            + "of camera behaviors.")]
        public float3 damping;

        /// <summary>"How aggressively the camera tries to track the target rotation's X angle.
        /// Small numbers are more responsive.  Larger numbers give a more heavy slowly responding camera.</summary>
        [Tooltip("How aggressively the camera tries to track the target rotation.  "
            + "Small numbers are more responsive.  Larger numbers give a more heavy slowly responding camera.")]
        public float angularDamping;

        /// <summary>Different ways to set the camera's up vector</summary>
        public enum CameraUpMode
        {
            /// <summary>Leave the camera's up vector alone.  It will be set according to the Brain's WorldUp.</summary>
            Default,
            /// <summary>Take the up vector from the path's up vector at the current point</summary>
            Path,
            /// <summary>Take the up vector from the path's up vector at the current point, but with the roll zeroed out</summary>
            PathNoRoll,
            /// <summary>Take the up vector from the Follow target's up vector</summary>
            FollowTarget,
            /// <summary>Take the up vector from the Follow target's up vector, but with the roll zeroed out</summary>
            FollowTargetNoRoll,
        };

        /// <summary>How to set the virtual camera's Up vector.  This will affect the screen composition.</summary>
        [Tooltip("How to set the virtual camera's Up vector.  This will affect the screen composition, "
            + "because the camera Aim behaviours will always try to respect the Up direction.")]
        public CameraUpMode cameraUp;

        /// <summary>Controls how automatic dollying occurs</summary>
        [Serializable]
        public struct AutoDolly
        {
            /// <summary>If checked, will enable automatic dolly, which chooses a path position
            /// that is as close as possible to the Follow target.</summary>
            [Tooltip("If checked, will enable automatic dolly, which chooses a path position "
                + "that is as close as possible to the Follow target")]
            public bool enabled;

            /// <summary>Offset, in current position units, from the closest point on the path to the follow target.</summary>
            [Tooltip("Offset, in current position units, from the closest point on the path to the follow target")]
            public float positionOffset;

            /// <summary>Search up to how many waypoints on either side of the current position.  Use 0 for Entire path</summary>
            [Tooltip("Search up to how many waypoints on either side of the current position.  "
               + "Use 0 for Entire path.")]
            public int searchRadius;

            /// <summary>We search between waypoints by dividing the segment into this many straight pieces.
            /// The higher the number, the more accurate the result, but performance is
            /// proportionally slower for higher numbers</summary>
            [Tooltip("We search between waypoints by dividing the segment into this many straight pieces.  "
                + "The higher the number, the more accurate the result, but performance is proportionally "
                + "slower for higher numbers")]
            public int searchResolution;
        };

        /// <summary>Controls how automatic dollying occurs</summary>
        [Tooltip("Controls how automatic dollying occurs.  A Follow target is necessary to use this feature.")]
        public AutoDolly autoDolly;
    }

    [Serializable]
    public struct CM_VcamTrackedDollyState : IComponentData
    {
        public quaternion previousOrientation;
        public float3 previousCameraPosition;
        public float previousPathPosition;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    [UpdateBefore(typeof(CM_VcamPreAimSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamTrackedDollySystem : JobComponentSystem
    {
        EntityQuery m_vcamGroup;
        EntityQuery m_missingStateGroup;
        EntityQuery m_missingTargetGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetEntityQuery(
                ComponentType.ReadWrite<CM_VcamPositionState>(),
                ComponentType.ReadWrite<CM_VcamRotationState>(),
                ComponentType.ReadWrite<CM_VcamTrackedDollyState>(),
                ComponentType.ReadWrite<CM_VcamTrackedDolly>(),
                ComponentType.ReadOnly<CM_VcamFollowTarget>(),
                ComponentType.ReadOnly<CM_VcamChannel>());

            m_missingStateGroup = GetEntityQuery(
                ComponentType.Exclude<CM_VcamTrackedDollyState>(),
                ComponentType.ReadOnly<CM_VcamTrackedDolly>());

            m_missingTargetGroup = GetEntityQuery(
                ComponentType.Exclude<CM_VcamFollowTarget>(),
                ComponentType.ReadOnly<CM_VcamTrackedDolly>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing transposer state components
            EntityManager.AddComponent(m_missingStateGroup,
                ComponentType.ReadWrite<CM_VcamTrackedDollyState>());
            EntityManager.AddComponent(m_missingTargetGroup,
                ComponentType.ReadWrite<CM_VcamFollowTarget>());

            var targetSystem = World.GetOrCreateSystem<CM_TargetSystem>();
            var targetLookup = targetSystem.GetTargetLookupForJobs(ref inputDeps);
            if (!targetLookup.IsCreated)
                return inputDeps; // no targets yet

            var channelSystem = World.GetOrCreateSystem<CM_ChannelSystem>();
            JobHandle vcamDeps = channelSystem.InvokePerVcamChannel(
                m_vcamGroup, inputDeps, new DollyJobLaunch
                {
                    system = this,
                    targetLookup = targetLookup
                });

            return targetSystem.RegisterTargetLookupReadJobs(vcamDeps);
        }

        struct DollyJobLaunch : CM_ChannelSystem.VcamGroupCallback
        {
            public CM_VcamTrackedDollySystem system;
            public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;
            public JobHandle Invoke(
                EntityQuery filteredGroup, Entity channelEntity,
                CM_Channel c, CM_ChannelState state, JobHandle inputDeps)
            {
                var job = new DollyJob
                {
                    deltaTime = state.deltaTime,
                    up = math.mul(c.settings.worldOrientation, math.up()),
                    targetLookup = targetLookup,
                    paths = system.GetComponentDataFromEntity<CM_PathState>(true),
                    waypoints = system.GetBufferFromEntity<CM_PathWaypointElement>(true)
                };
                return job.Schedule(filteredGroup, inputDeps);
            }
        }

        [BurstCompile]
        struct DollyJob : IJobForEachWithEntity<
            CM_VcamPositionState, CM_VcamRotationState, CM_VcamTrackedDollyState,
            CM_VcamTrackedDolly, CM_VcamFollowTarget>
        {
            public float deltaTime;
            public float3 up;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;
            [ReadOnly] public ComponentDataFromEntity<CM_PathState> paths;
            [ReadOnly] public BufferFromEntity<CM_PathWaypointElement> waypoints;

            public void Execute(
                Entity entity, int intex,
                ref CM_VcamPositionState posState,
                ref CM_VcamRotationState rotState,
                ref CM_VcamTrackedDollyState dollyState,
                ref CM_VcamTrackedDolly dolly,
                [ReadOnly] ref CM_VcamFollowTarget follow)
            {
                if (!waypoints.Exists(dolly.path) || ! paths.Exists(dolly.path))
                    return; // no path
                DynamicBuffer<CM_PathWaypointElement> wp = waypoints[dolly.path];
                CM_PathState pathState = paths[dolly.path];

                float dt = math.select(-1, deltaTime, posState.previousFrameDataIsValid);

                // Auto-dolly: get the new ideal path base position if valid follow target
                if (dolly.autoDolly.enabled
                    && targetLookup.TryGetValue(follow.target, out CM_TargetSystem.TargetInfo targetInfo))
                {
                    // This works in path units
                    float prevPos = CM_PathSystem.ToNativePathUnits(
                        ref wp, ref pathState, dollyState.previousPathPosition, dolly.positionUnits);
                    var radius = dolly.autoDolly.searchRadius;
                    dolly.pathPosition = CM_PathSystem.FindClosestPoint(
                        targetInfo.position,
                        (int)math.floor(prevPos),
                        math.select(radius, -1, dt < 0 || radius <= 0),
                        dolly.autoDolly.searchResolution,
                        ref pathState, ref wp);

                    dolly.pathPosition = CM_PathSystem.FromPathNativeUnits(
                        ref wp, ref pathState, dolly.pathPosition, dolly.positionUnits);

                    // Apply the path position offset
                    dolly.pathPosition += dolly.autoDolly.positionOffset;
                }

                // Where to go on the path
                float newPathPosition = dolly.pathPosition;
                if (dt >= 0)
                {
                    // Normalize previous position to find the shortest path
                    float maxUnit = CM_PathSystem.MaxUnit(ref wp, ref pathState, dolly.positionUnits);
                    float next = CM_PathSystem.ClampUnit(
                        ref wp, ref pathState, newPathPosition, dolly.positionUnits);
                    float prev = CM_PathSystem.ClampUnit(
                        ref wp, ref pathState, dollyState.previousPathPosition, dolly.positionUnits);
                    prev = math.select(
                        prev, math.select(prev - maxUnit, prev + maxUnit, next > prev),
                        pathState.looped && math.abs(next - prev) > maxUnit / 2);

                    dollyState.previousPathPosition = prev;
                    newPathPosition = next;

                    // Apply damping along the path direction
                    float delta = newPathPosition - dollyState.previousPathPosition;
                    delta = MathHelpers.Damp(delta, dolly.damping.z, dt);
                    newPathPosition = dollyState.previousPathPosition + delta;
                }
                dollyState.previousPathPosition = newPathPosition;

                // Get the path info at the new position
                float nativePathPos = CM_PathSystem.ToNativePathUnits(
                    ref wp, ref pathState, newPathPosition, dolly.positionUnits);
                float3 newCameraPos
                    = CM_PathSystem.EvaluatePosition(nativePathPos, ref pathState, ref wp);
                var newPathOrientation
                    = CM_PathSystem.EvaluateOrientation(nativePathPos, ref pathState, ref wp);

                // Apply the offset to get the new camera position
                newCameraPos += math.mul(newPathOrientation, dolly.pathOffset);

                // Apply damping to the remaining directions
                if (dt >= 0)
                {
                    var delta = newCameraPos - dollyState.previousCameraPosition;
                    delta = math.mul(newPathOrientation, delta);
                    delta.xy = MathHelpers.Damp(delta.xy, dolly.damping.xy, dt);
                    delta = math.mul(math.inverse(newPathOrientation), delta);
                    newCameraPos = dollyState.previousCameraPosition + delta;
                }
                posState.raw = dollyState.previousCameraPosition = newCameraPos;

                // Set the orientation and up
                var newOrientation = GetCameraOrientationAtPathPoint(
                    newPathOrientation, up, dolly.cameraUp, newPathOrientation, rotState.raw);
                float t = MathHelpers.Damp(1, dolly.angularDamping, dt);
                newOrientation = math.slerp(dollyState.previousOrientation, newOrientation, t);
                rotState.raw = dollyState.previousOrientation = newOrientation;
                posState.up = math.select(
                    math.mul(newOrientation, math.up()), up,
                    dolly.cameraUp == CM_VcamTrackedDolly.CameraUpMode.Default);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static quaternion GetCameraOrientationAtPathPoint(
                quaternion pathOrientation, float3 up,
                CM_VcamTrackedDolly.CameraUpMode upMode,
                quaternion followTargetRotation, quaternion cameraOrientation)
            {
                switch (upMode)
                {
                    default: break;
                    case CM_VcamTrackedDolly.CameraUpMode.Path:
                        return pathOrientation;
                    case CM_VcamTrackedDolly.CameraUpMode.PathNoRoll:
                        return quaternion.LookRotation(math.mul(pathOrientation, new float3(0, 0, 1)), up);
                    case CM_VcamTrackedDolly.CameraUpMode.FollowTarget:
                        return followTargetRotation;
                    case CM_VcamTrackedDolly.CameraUpMode.FollowTargetNoRoll:
                        return quaternion.LookRotation(math.mul(followTargetRotation, new float3(0, 0, 1)), up);
                }
                return quaternion.LookRotation(math.mul(cameraOrientation, new float3(0, 0, 1)), up);
            }
        }
    }
}
