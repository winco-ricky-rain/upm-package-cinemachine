using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Cinemachine.ECS
{
    public class CM_EntityVcam : ICinemachineCamera
    {
        public Entity entity;

        public CM_EntityVcam(Entity e) { entity = e; }
        public Entity Entity { get { return entity; } }

        public string Name { get { return entity.ToString(); } }
        public string Description { get { return ""; }}
        public CameraState State { get { return StateFromEntity(entity); } }

        public bool IsValid { get { return entity != Entity.Null; } }

        public ICinemachineCamera ParentCamera { get { return null; } }
        public bool IsLiveChild(ICinemachineCamera vcam) { return false; }

        public void UpdateCameraState(Vector3 worldUp, float deltaTime) {}
        public void InternalUpdateCameraState(Vector3 worldUp, float deltaTime) {}
        public void OnTransitionFromCamera(ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime) {}
        public void OnTargetObjectWarped(Transform target, Vector3 positionDelta) {}
        public bool IsLive
        {
            get
            {
                var m = ActiveChannelSystem;
                return m == null ? false : m.IsLive(this);
            }
        }
        public Entity AsEntity { get { return Entity; }}

        static CM_ChannelSystem ActiveChannelSystem
        {
            get { return World.Active?.GetExistingManager<CM_ChannelSystem>(); }
        }

        public static CameraState StateFromEntity(Entity e)
        {
            CameraState state = CameraState.Default;
            var m = World.Active?.GetExistingManager<EntityManager>();
            if (m != null && e != Entity.Null)
            {
                // Is this entity a channel?
                if (m.HasComponent<CM_ChannelBlendState>(e) && m.HasComponent<CM_Channel>(e))
                {
                    if (m.GetSharedComponentData<CM_VcamChannel>(e).channel
                            != m.GetComponentData<CM_Channel>(e).channel)
                    {
                        var blendState = m.GetComponentData<CM_ChannelBlendState>(e);
                        return blendState.blender.State.cameraState;
                    }
                }

                bool noLens = true;
                if (m.HasComponent<CM_VcamLensState>(e))
                {
                    var c = m.GetComponentData<CM_VcamLensState>(e);
                    state.Lens = new LensSettings
                    {
                        FieldOfView = c.fov,
                        OrthographicSize = c.fov,
                        NearClipPlane = c.nearClip,
                        FarClipPlane = c.farClip,
                        Dutch = c.dutch,
                        LensShift = c.lensShift,
                        Orthographic = c.orthographic != 0,
                        SensorSize = new Vector2(c.aspect, 1f) // GML todo: physical camera
                    };
                    noLens = false;
                }
                if (m.HasComponent<CM_VcamPositionState>(e))
                {
                    var c = m.GetComponentData<CM_VcamPositionState>(e);
                    state.RawPosition = c.raw;
                    state.ReferenceUp = c.up;
                    state.PositionCorrection = c.correction;
                }
                if (m.HasComponent<CM_VcamRotationState>(e))
                {
                    var c = m.GetComponentData<CM_VcamRotationState>(e);
                    state.ReferenceLookAt = c.lookAtPoint;
                    state.RawOrientation = math.normalizesafe(c.raw);
                    state.OrientationCorrection = math.normalizesafe(c.correction);
                }
                if (m.HasComponent<CM_VcamBlendHint>(e))
                {
                    var c = m.GetComponentData<CM_VcamBlendHint>(e);
                    state.BlendHint = (CameraState.BlendHintValue)c.blendHint; // GML fixme
                    if (noLens)
                        state.BlendHint |= CameraState.BlendHintValue.NoLens;
                }
                if (m.HasComponent<CM_VcamShotQuality>(e))
                {
                    var c = m.GetComponentData<CM_VcamShotQuality>(e);
                    state.ShotQuality = c.value;
                }
            }
            return state;
        }


        // GML hack until I think of something better
        static Dictionary<Entity, ICinemachineCamera> sVcamCache;
        public static ICinemachineCamera GetEntityVcam(Entity e)
        {
            if (sVcamCache == null)
                sVcamCache = new Dictionary<Entity, ICinemachineCamera>();
            ICinemachineCamera vcam = null;
            if (e != Entity.Null && !sVcamCache.TryGetValue(e, out vcam))
                sVcamCache[e] = vcam = new CM_EntityVcam(e);
            return vcam;
        }
        public static void RegisterEntityVcam(ICinemachineCamera vcam)
        {
            if (vcam != null)
            {
                if (sVcamCache == null)
                    sVcamCache = new Dictionary<Entity, ICinemachineCamera>();
                var e = vcam.AsEntity;
                if (e != Entity.Null)
                    sVcamCache[e] = vcam;
            }
        }
        public static void UnregisterEntityVcam(ICinemachineCamera vcam)
        {
            if (vcam != null)
            {
                var e = vcam.AsEntity;
                if (e != Entity.Null && sVcamCache.ContainsKey(e))
                    sVcamCache.Remove(e);
            }
        }
    }
}
