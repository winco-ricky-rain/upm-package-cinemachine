using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System;
using UnityEngine;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_VcamPerlinNoise : IComponentData
    {
        /// <summary>
        /// Gain to apply to the amplitudes defined in the settings asset.
        /// </summary>
        [Tooltip("Gain to apply to the amplitudes defined in the NoiseSettings asset.  "
            + "1 is normal.  Setting this to 0 completely mutes the noise.")]
        public float amplitudeGain;

        /// <summary>
        /// Scale factor to apply to the frequencies defined in the settings asset.
        /// </summary>
        [Tooltip("Scale factor to apply to the frequencies defined in the NoiseSettings asset.  "
            + "1 is normal.  Larger magnitudes will make the noise shake more rapidly.")]
        public float frequencyGain;

        /// <summary>
        /// Length of the arm that holds the camera.  Rotation occurs at the base of the arm.
        /// </summary>
        [Tooltip("Length of the arm that holds the camera.  Rotation occurs at the base of the arm.")]
        public float armLength;

        /// <summary>
        /// This guarantees repeatability of the noise, variety between cameras.
        /// </summary>
        [Tooltip("This guarantees repeatability of the noise, variety between cameras.")]
        public float noiseSeed;
    }

    [Serializable]
    public struct CM_VcamPerlinNoiseDefinition : ISharedComponentData
    {
        /// <summary>
        /// Serialized property for referencing a NoiseSettings asset
        /// </summary>
        [Tooltip("The asset containing the Noise Profile.  Define the frequencies and amplitudes "
            + "there to make a characteristic noise profile.  "
            + "Make your own or just use one of the many presets.")]
        [NoiseSettingsProperty]
        public NoiseSettings noiseProfile;
    }

    [Serializable]
    public struct CM_VcamPerlinNoiseState : ISystemStateComponentData
    {
        public float noiseTime;
    }

    [ExecuteAlways]
    [UpdateAfter(typeof(CM_VcamPreCorrectionSystem))]
    [UpdateBefore(typeof(CM_VcamFinalizeSystem))]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_VcamPerlinNoiseSystem : JobComponentSystem
    {
        ComponentGroup m_vcamGroup;
        ComponentGroup m_missingStateGroup;
        EndSimulationEntityCommandBufferSystem m_missingStateBarrier;

        protected override void OnCreateManager()
        {
            m_vcamGroup = GetComponentGroup(
                ComponentType.ReadWrite<CM_VcamRotationState>(),
                ComponentType.ReadWrite<CM_VcamPositionState>(),
                ComponentType.ReadWrite<CM_VcamPerlinNoiseState>(),
                ComponentType.ReadOnly<CM_VcamPerlinNoise>(),
                ComponentType.ReadOnly<CM_VcamPerlinNoiseDefinition>(),
                ComponentType.ReadOnly<CM_VcamChannel>());

            m_missingStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_VcamPerlinNoise>(),
                ComponentType.Exclude<CM_VcamPerlinNoiseState>());

            m_missingStateBarrier = World.GetOrCreateManager<EndSimulationEntityCommandBufferSystem>();
        }

        List<CM_VcamPerlinNoiseDefinition> uniqueTypes;
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing state components
            if (m_missingStateGroup.CalculateLength() > 0)
            {
                var cb  = m_missingStateBarrier.CreateCommandBuffer();
                var a = m_missingStateGroup.ToEntityArray(Allocator.TempJob);
                for (int i = 0; i < a.Length; ++i)
                    cb.AddComponent(a[i], new CM_VcamPerlinNoiseState());
                a.Dispose();
            }

            if (uniqueTypes == null)
                uniqueTypes = new List<CM_VcamPerlinNoiseDefinition>();
            uniqueTypes.Clear();
            EntityManager.GetAllUniqueSharedComponentData(uniqueTypes);

            JobHandle vcamDeps = inputDeps;
            var channelSystem = World.GetOrCreateManager<CM_ChannelSystem>();

            for (int i = 0; i < uniqueTypes.Count; ++i)
            {
                channelSystem.InvokePerVcamChannel(
                    m_vcamGroup, uniqueTypes[i],
                    (ComponentGroup filteredGroup, Entity e, CM_Channel c, CM_ChannelState state) =>
                    {
                        var profile = uniqueTypes[i].noiseProfile;
                        if (profile != null)
                        {
                            var job = new PerlinNoiseJob()
                            {
                                deltaTime = state.deltaTime
                            };
                            if (profile.OrientationNoise.Length > 0)
                                job.rotNoise0 = profile.OrientationNoise[0];
                            if (profile.OrientationNoise.Length > 1)
                                job.rotNoise1 = profile.OrientationNoise[1];
                            if (profile.OrientationNoise.Length > 2)
                                job.rotNoise2 = profile.OrientationNoise[2];
                            vcamDeps = job.ScheduleGroup(filteredGroup, vcamDeps);
                        }
                    });
            }
            return vcamDeps;
        }

        [BurstCompile]
        struct PerlinNoiseJob : IJobProcessComponentData<
            CM_VcamPositionState, CM_VcamRotationState, CM_VcamPerlinNoiseState, CM_VcamPerlinNoise>
        {
            public NoiseSettings.TransformNoiseParams rotNoise0;
            public NoiseSettings.TransformNoiseParams rotNoise1;
            public NoiseSettings.TransformNoiseParams rotNoise2;
            public float deltaTime;

            public void Execute(
                ref CM_VcamPositionState posState,
                ref CM_VcamRotationState rotState,
                ref CM_VcamPerlinNoiseState noiseState,
                [ReadOnly] ref CM_VcamPerlinNoise noise)
            {
                if (deltaTime < 0)
                    return;

                noiseState.noiseTime += deltaTime * noise.frequencyGain;
                var e = NoiseAt(rotNoise0, noiseState.noiseTime, noise.noiseSeed)
                    + NoiseAt(rotNoise1, noiseState.noiseTime, noise.noiseSeed)
                    + NoiseAt(rotNoise2, noiseState.noiseTime, noise.noiseSeed);
                var q = quaternion.Euler(math.radians(e * noise.amplitudeGain));
                float3 arm = new float3(0, 0, noise.armLength);
                posState.correction += (math.mul(q, arm) - arm);
                rotState.correction = math.mul(rotState.correction, q);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            float3 NoiseAt(NoiseSettings.TransformNoiseParams n, float time, float offset)
            {
                return new float3(
                    n.X.GetValueAt(time, offset),
                    n.Y.GetValueAt(time, offset),
                    n.Z.GetValueAt(time, offset));
            }
        }
    }
}
