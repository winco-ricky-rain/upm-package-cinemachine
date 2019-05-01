using UnityEngine;
using System;
using Unity.Mathematics;
using Unity.Entities;
using Cinemachine;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    /// <summary>
    /// FreeLook version of the virtual camera.
    /// Note: this implementation sucks.  It should be done with some kind of generic reactor.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [SaveDuringPlay]
    [AddComponentMenu("Cinemachine/CM_FreeLook")]
    public class CM_FreeLook : CM_BasicFreeLook
    {
        [Header("Rig Overrides")]
        public CM_FreeLookRigBlendableSettings topRig;
        public CM_FreeLookRigBlendableSettings bottomRig;

        bool haveMiddleRigSnapshot;
        CM_FreeLookRigBlendableSettings middleRig;

        protected override void OnValidate()
        {
            base.OnValidate();
            topRig.Validate();
            bottomRig.Validate();
        }

        protected override void Reset()
        {
            base.Reset();
            topRig = new CM_FreeLookRigBlendableSettings();
            bottomRig = new CM_FreeLookRigBlendableSettings();
        }

        protected void OnEnable()
        {
            haveMiddleRigSnapshot = false;
        }

        protected override void Update()
        {
            base.Update();
            if (!Application.isPlaying)
                return;

            Entity e = Entity;
            if (!haveMiddleRigSnapshot)
                haveMiddleRigSnapshot = middleRig.PullFrom(e);

            if (haveMiddleRigSnapshot)
            {
                float blendAmount = 0.5f;
                var ch = new ConvertEntityHelper(transform);
                if (ch.HasComponent<CM_VcamOrbital>())
                {
                    var c = ch.SafeGetComponentData<CM_VcamOrbital>();
                    blendAmount = c.verticalAxis.GetNormalizedValue();
                }

                CM_FreeLookRigBlendableSettings otherRig;
                if (blendAmount < 0.5f)
                {
                    blendAmount = 1 - (blendAmount * 2);
                    otherRig = bottomRig;
                }
                else
                {
                    blendAmount = (blendAmount - 0.5f) * 2f;
                    otherRig = topRig;
                }

                // Blend the components
                CM_FreeLookRigBlendableSettings result = middleRig;
                result.LerpTo(ref otherRig, blendAmount);
                result.PushTo(e);
            }
        }
    }

    /// <summary>Override settings for top and bottom rigs</summary>
    [Serializable]
    public struct CM_FreeLookRigBlendableSettings
    {
        public bool customLens;
        public LensSettings lens;
        public bool customBody;
        public BlendableOrbitalSettings body;
        public bool customAim;
        public BlendableComposerSettings aim;
        public bool customNoise;
        public BlendableNoiseSettings noise;

        public void Validate()
        {
            if (lens.FieldOfView == 0)
                lens = LensSettings.Default;
            lens.Validate();
        }

        public bool PullFrom(Entity e)
        {
            var m = World.Active?.EntityManager;
            if (m == null || !m.Exists(e))
                return false;
            if (m.HasComponent<CM_VcamLens>(e))
            {
                var c = m.GetComponentData<CM_VcamLens>(e);
                lens = new LensSettings
                {
                    FieldOfView = c.fov,
                    OrthographicSize = c.fov,
                    NearClipPlane = c.nearClip,
                    FarClipPlane = c.farClip,
                    Dutch = c.dutch,
                    LensShift = c.lensShift
                };
            }
            if (m.HasComponent<CM_VcamOrbital>(e))
            {
                var c = m.GetComponentData<CM_VcamOrbital>(e);
                body.PullFrom(ref c);
            }
            if (m.HasComponent<CM_VcamComposer>(e))
            {
                var c = m.GetComponentData<CM_VcamComposer>(e);
                aim.PullFrom(ref c);
            }
            if (m.HasComponent<CM_VcamPerlinNoise>(e))
            {
                var c = m.GetComponentData<CM_VcamPerlinNoise>(e);
                noise.PullFrom(ref c);
            }
            return true;
        }

        public bool PushTo(Entity e)
        {
            var m = World.Active?.EntityManager;
            if (m == null || !m.Exists(e))
                return false;
            if (m.HasComponent<CM_VcamLens>(e))
            {
                var c = m.GetComponentData<CM_VcamLens>(e);
                c.fov = lens.Orthographic ? lens.OrthographicSize : lens.FieldOfView;
                c.nearClip = lens.NearClipPlane;
                c.farClip = lens.FarClipPlane;
                c.dutch = lens.Dutch;
                c.lensShift = lens.LensShift;
                m.SetComponentData(e, c);
            }
            if (m.HasComponent<CM_VcamOrbital>(e))
            {
                var c = m.GetComponentData<CM_VcamOrbital>(e);
                body.PushTo(ref c);
                m.SetComponentData(e, c);
            }
            if (m.HasComponent<CM_VcamComposer>(e))
            {
                var c = m.GetComponentData<CM_VcamComposer>(e);
                aim.PushTo(ref c);
                m.SetComponentData(e, c);
            }
            if (m.HasComponent<CM_VcamPerlinNoise>(e))
            {
                var c = m.GetComponentData<CM_VcamPerlinNoise>(e);
                noise.PushTo(ref c);
                m.SetComponentData(e, c);
            }
            return true;
        }

        public void LerpTo(ref CM_FreeLookRigBlendableSettings o, float t)
        {
            if (o.customLens)
            {
                lens.FieldOfView = math.lerp(lens.FieldOfView, o.lens.FieldOfView, t);
                lens.OrthographicSize = math.lerp(lens.OrthographicSize, o.lens.OrthographicSize, t);
                lens.NearClipPlane = math.lerp(lens.NearClipPlane, o.lens.NearClipPlane, t);
                lens.FarClipPlane = math.lerp(lens.FarClipPlane, o.lens.FarClipPlane, t);
                lens.Dutch = math.lerp(lens.Dutch, o.lens.Dutch, t);
                lens.LensShift = math.lerp(lens.LensShift, o.lens.LensShift, t);
            }
            if (o.customBody)
                body.LerpTo(ref o.body, t);
            if (o.customAim)
                aim.LerpTo(ref o.aim, t);
            if (o.customNoise)
                noise.LerpTo(ref o.noise, t);
        }

        /// <summary>Blendable settings for Orbital</summary>
        [Serializable] public struct BlendableOrbitalSettings
        {
            public float3 damping;
            public float angularDamping;

            public void LerpTo(ref BlendableOrbitalSettings o, float t)
            {
                damping = math.lerp(damping, o.damping, t);
                angularDamping = math.lerp(angularDamping, o.angularDamping, t);
            }

            public void PullFrom(ref CM_VcamOrbital o)
            {
                damping = o.damping;
                angularDamping = o.angularDamping;
            }

            public void PushTo(ref CM_VcamOrbital o)
            {
                o.damping = damping;
                o.angularDamping = angularDamping;
            }
        }

        /// <summary>Blendable settings for Composer</summary>
        [Serializable] public struct BlendableComposerSettings
        {
            public float2 damping;
            public float2 screenPosition;
            public float2 deadZoneSize;
            public float2 softZoneSize;
            public float2 softZoneBias;

            public void LerpTo(ref BlendableComposerSettings c, float t)
            {
                damping = math.lerp(c.damping, damping, t);
                screenPosition = math.lerp(screenPosition, c.screenPosition, t);
                deadZoneSize = math.lerp(deadZoneSize, c.deadZoneSize, t);
                softZoneSize = math.lerp(softZoneSize, c.softZoneSize, t);
                softZoneBias = math.lerp(softZoneBias, c.softZoneBias, t);
            }

            public void PullFrom(ref CM_VcamComposer c)
            {
                damping = c.damping;
                screenPosition = c.screenPosition;
                deadZoneSize = c.deadZoneSize;
                softZoneSize = c.softZoneSize;
                softZoneBias = c.softZoneBias;
            }

            public void PushTo(ref CM_VcamComposer c)
            {
                c.damping = damping;
                c.screenPosition = screenPosition;
                c.deadZoneSize = deadZoneSize;
                c.softZoneSize = softZoneSize;
                c.softZoneBias = softZoneBias;
            }
        }

        /// <summary>Blendable settings for CinemachineBasicMultiChannelPerlin</summary>
        [Serializable] public struct BlendableNoiseSettings
        {
            public float amplitudeGain;
            public float frequencyGain;

            public void LerpTo(ref BlendableNoiseSettings p, float t)
            {
                amplitudeGain = math.lerp(amplitudeGain, p.amplitudeGain, t);
                frequencyGain =math.lerp(frequencyGain, p.frequencyGain, t);
            }

            public void PullFrom(ref CM_VcamPerlinNoise p)
            {
                amplitudeGain = p.amplitudeGain;
                frequencyGain = p.frequencyGain;
            }

            public void PushTo(ref CM_VcamPerlinNoise p)
            {
                p.amplitudeGain = amplitudeGain;
                p.frequencyGain = frequencyGain;
            }
        }
    }
}
