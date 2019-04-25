using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using UnityEngine;
using System;

namespace Unity.Cinemachine3
{
    [Serializable]
    public struct CM_VcamOrbital : IComponentData
    {
        /// <summary>The coordinate space to use when interpreting the offset from the target</summary>
        public CM_VcamTransposerSystem.BindingMode bindingMode;

        /// <summary>How aggressively the camera tries to maintain the offset in the 3 axes.
        /// Small numbers are more responsive, rapidly translating the camera to keep the target's
        /// offset.  Larger numbers give a more heavy slowly responding camera.
        /// Using different settings per axis can yield a wide range of camera behaviors</summary>
        public float3 damping;

        /// <summary>How aggressively the camera tries to track the target's rotation.
        /// Small numbers are more responsive.  Larger numbers give a more heavy slowly responding camera.</summary>
        public float angularDamping;

        /// <summary>Defines the height and radius for an orbit</summary>
        [Serializable]
        public struct Orbit
        {
            /// <summary>Height relative to target</summary>
            public float height;

            /// <summary>Radius of orbit</summary>
            public float radius;
        }

        public Orbit top;
        public Orbit middle;
        public Orbit bottom;

        /// <summary></summary>
        [Tooltip("Controls how taut is the line that connects the rigs' orbits, which determines "
            + "final placement on the Y axis")]
        [Range(0f, 1f)]
        public float splineCurvature;

        /// <summary>The Horizontal axis.  -180..180.  0 is the center.
        /// Rotates the camera horizontally around the target</summary>
        [Tooltip("The Horizontal axis.  Value is -180..180.  0 is the center.  "
            + "Rotates the camera horizontally around the target")]
        public CM_InputAxis horizontalAxis;

        /// <summary>The Vertical axis.  Value is -1..1.</summary>
        [Tooltip("The Vertical axis.  Value is -1..1.  0.5 is the middle rig.")]
        public CM_InputAxis verticalAxis;

        /// <summary>The Radial axis.  Scales the orbits.  Value is the base radius of the orbits</summary>
        [Tooltip("The Radial axis.  Scales the orbits.  Value is the base radius of the orbits")]
        public CM_InputAxis radialAxis;
    }

    [Serializable]
    public unsafe struct CM_VcamOrbitalState : IComponentData
    {
        /// State information used for damping
        public float3 previousTargetPosition;
        public quaternion previousTargetRotation;
        public float3 previousTargetOffset;

        // Cached spline
        fixed float knots[4 * 5];
        fixed float ctrl1[4 * 5];
        fixed float ctrl2[4 * 5];
        fixed float scratch[4 * 5];
        CM_VcamOrbital.Orbit top;
        CM_VcamOrbital.Orbit middle;
        CM_VcamOrbital.Orbit bottom;
        float tension;

        public void UpdateCachedSpline(ref CM_VcamOrbital src)
        {
            bool cacheIsValid
                = tension == src.splineCurvature
                && top.height == src.top.height && top.radius == src.top.radius
                && middle.height == src.middle.height && middle.radius == src.middle.radius
                && bottom.height == src.bottom.height && bottom.radius == src.bottom.radius;
            if (!cacheIsValid)
            {
                top = src.top;
                middle = src.middle;
                bottom = src.bottom;
                tension = src.splineCurvature;

                fixed (void* k = knots, c1 = ctrl1, c2 = ctrl2, s = scratch)
                {
                    float4* kk = (float4*)k;
                    kk[1] = new float4(0, bottom.height, -bottom.radius, 0);
                    kk[2] = new float4(0, middle.height, -middle.radius, 0);
                    kk[3] = new float4(0, top.height, -top.radius, 0);
                    kk[0] = math.lerp(kk[1], float4.zero, tension);
                    kk[4] = math.lerp(kk[3], float4.zero, tension);
                    BezierHelpers.ComputeSmoothControlPoints(
                        (float4*)k, (float4*)c1, (float4*)c2, (float4*)s, 5);
                }
            }
        }

        // t ranges from -1 to 1
        public float3 SplineValueAt(float t)
        {
            int n = math.select(2, 1, t < 0) * 4;
            float3 pos = MathHelpers.Bezier(math.select(t, t + 1, t < 0),
                new float3(knots[n], knots[n + 1], knots[n + 2]),
                new float3(ctrl1[n], ctrl1[n + 1], ctrl1[n + 2]),
                new float3(ctrl2[n], ctrl2[n + 1], ctrl2[n + 2]),
                new float3(knots[n+4], knots[n+5], knots[n+6]));
            return pos;
        }
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreBodySystem))]
    [UpdateBefore(typeof(CM_VcamPreAimSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamOrbitalSystem : JobComponentSystem
    {
        EntityQuery m_vcamGroup;
        EntityQuery m_missingStateGroup;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetEntityQuery(
                ComponentType.ReadWrite<CM_VcamPositionState>(),
                ComponentType.ReadWrite<CM_VcamOrbitalState>(),
                ComponentType.ReadWrite<CM_VcamOrbital>(),
                ComponentType.ReadOnly<CM_VcamFollowTarget>(),
                ComponentType.ReadOnly<CM_VcamChannel>());

            m_missingStateGroup = GetEntityQuery(
                ComponentType.Exclude<CM_VcamOrbitalState>(),
                ComponentType.ReadOnly<CM_VcamOrbital>());
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing orbital state components
            EntityManager.AddComponent(m_missingStateGroup,
                ComponentType.ReadWrite<CM_VcamOrbitalState>());

            var targetSystem = World.GetOrCreateSystem<CM_TargetSystem>();
            var targetLookup = targetSystem.GetTargetLookupForJobs(ref inputDeps);
            if (!targetLookup.IsCreated)
                return inputDeps; // no targets yet

            var channelSystem = World.GetOrCreateSystem<CM_ChannelSystem>();
            JobHandle vcamDeps = channelSystem.ScheduleForVcamsOnAllChannels(
                m_vcamGroup, inputDeps, new TrackTargetJob
                {
                    timeNow = Time.time,
                    targetLookup = targetLookup
                });

            return targetSystem.RegisterTargetLookupReadJobs(vcamDeps);
        }

        [BurstCompile]
        struct TrackTargetJob : CM_ChannelSystem.IVcamPerChannelJob, IJobForEach<
            CM_VcamPositionState, CM_VcamOrbitalState,
            CM_VcamOrbital, CM_VcamFollowTarget>
        {
            public float deltaTime;
            public float timeNow;
            public float3 up;
            [ReadOnly] public NativeHashMap<Entity, CM_TargetSystem.TargetInfo> targetLookup;

            public bool InitializeForChannel(
                Entity channelEntity, CM_Channel c, CM_ChannelState state)
            {
                deltaTime = state.deltaTime;
                up = math.mul(c.settings.worldOrientation, math.up());
                return true;
            }

            public void Execute(
                ref CM_VcamPositionState posState,
                ref CM_VcamOrbitalState orbitalState,
                ref CM_VcamOrbital orbital,
                [ReadOnly] ref CM_VcamFollowTarget follow)
            {
                if (!targetLookup.TryGetValue(follow.target, out CM_TargetSystem.TargetInfo targetInfo))
                    return;

                float dt = math.select(-1, deltaTime, posState.previousFrameDataIsValid);

                var targetPos = targetInfo.position;
                var directionToTarget = math.select(
                    targetPos - posState.raw, -orbitalState.previousTargetOffset,
                    dt >= 0 && posState.previousFrameDataIsValid);
                var targetRot = CM_VcamTransposerSystem.GetRotationForBindingMode(
                        targetInfo.rotation, orbital.bindingMode, directionToTarget, up);

                bool isSimpleFollow = orbital.bindingMode == CM_VcamTransposerSystem.BindingMode.SimpleFollowWithWorldUp;
                var prevPos = orbitalState.previousTargetPosition;
                targetRot = CM_VcamTransposerSystem.ApplyRotationDamping(
                    dt, 0,
                    math.select(0, orbital.angularDamping, dt >= 0 && !isSimpleFollow),
                    orbitalState.previousTargetRotation, targetRot);
                targetPos = CM_VcamTransposerSystem.ApplyPositionDamping(
                    dt, 0,
                    math.select(float3.zero, orbital.damping, dt >= 0),
                    prevPos, targetPos, targetRot);

                orbital.radialAxis.DoRecentering(deltaTime, timeNow);
                orbital.verticalAxis.DoRecentering(deltaTime, timeNow);

                float heading = orbital.horizontalAxis.GetClampedValue();
                orbital.horizontalAxis.DoRecentering(deltaTime, timeNow);
                orbital.horizontalAxis.value = math.select(orbital.horizontalAxis.value, 0, isSimpleFollow);

                orbitalState.UpdateCachedSpline(ref orbital);

                float3 followOffset
                    = orbitalState.SplineValueAt(orbital.verticalAxis.GetNormalizedValue() * 2 - 1);
                followOffset *= orbital.radialAxis.GetClampedValue();
                quaternion q = quaternion.Euler(0, math.radians(heading), 0);
                followOffset = math.mul(q, followOffset);
                followOffset = math.mul(targetRot, followOffset);

                posState.raw = targetPos + followOffset;
                posState.up = math.mul(targetRot, math.up());
                posState.dampingBypass = followOffset - orbitalState.previousTargetOffset;

                orbitalState.previousTargetPosition = targetPos;
                orbitalState.previousTargetRotation = targetRot;
                orbitalState.previousTargetOffset = followOffset;
            }
        }
    }
}
