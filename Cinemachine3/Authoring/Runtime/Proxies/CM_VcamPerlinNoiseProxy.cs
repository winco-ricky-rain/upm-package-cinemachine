using UnityEngine;
using Cinemachine;
using Unity.Cinemachine.Common;
using Unity.Entities;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [CM_Pipeline(PipelineStage.Noise)]
    [SaveDuringPlay]
    [ExecuteAlways]
    public class CM_VcamPerlinNoiseProxy : CM_VcamComponentBase<CM_VcamPerlinNoise>
    {
        /// <summary>
        /// Serialized property for referencing a NoiseSettings asset
        /// </summary>
        [Tooltip("The asset containing the Noise Profile.  Define the frequencies and amplitudes "
            + "there to make a characteristic noise profile.  "
            + "Make your own or just use one of the many presets.")]
        [NoiseSettingsProperty]
        public NoiseSettings noiseProfile;

        public override void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            base.Convert(entity, dstManager, conversionSystem);
            dstManager.AddSharedComponentData(
                entity, new CM_VcamPerlinNoiseDefinition{ noiseProfile = noiseProfile });
        }

        private void Reset()
        {
            Value = new CM_VcamPerlinNoise
            {
                amplitudeGain = 1,
                frequencyGain = 1
            };
        }
    }
}
