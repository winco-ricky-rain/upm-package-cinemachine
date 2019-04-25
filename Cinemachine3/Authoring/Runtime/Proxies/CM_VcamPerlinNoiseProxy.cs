using UnityEngine;
using Cinemachine;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [CM_Pipeline(PipelineStage.Noise)]
    [SaveDuringPlay]
    [ExecuteAlways]
    public class CM_VcamPerlinNoiseProxy : CM_VcamComponentProxyBase<CM_VcamPerlinNoise>
    {
        /// <summary>
        /// Serialized property for referencing a NoiseSettings asset
        /// </summary>
        [Tooltip("The asset containing the Noise Profile.  Define the frequencies and amplitudes "
            + "there to make a characteristic noise profile.  "
            + "Make your own or just use one of the many presets.")]
        [NoiseSettingsProperty]
        public NoiseSettings noiseProfile;

        private void Reset()
        {
            Value = new CM_VcamPerlinNoise
            {
                amplitudeGain = 1,
                frequencyGain = 1
            };
        }

        protected virtual void PushValuesToEntityComponents()
        {
            var m = ActiveEntityManager;
            var e = Entity;
            if (m == null || !m.Exists(e))
                return;

            if (!m.HasComponent<CM_VcamPerlinNoiseDefinition>(e))
                m.AddSharedComponentData(e, new CM_VcamPerlinNoiseDefinition());

            var c = m.GetSharedComponentData<CM_VcamPerlinNoiseDefinition>(e);
            if (c.noiseProfile != noiseProfile)
                m.SetSharedComponentData(Entity, new CM_VcamPerlinNoiseDefinition
                {
                    noiseProfile = noiseProfile
                });
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            PushValuesToEntityComponents();
        }

        // GML: Needed in editor only, probably, only if something is dirtied
        void Update()
        {
            PushValuesToEntityComponents();
        }
    }
}
