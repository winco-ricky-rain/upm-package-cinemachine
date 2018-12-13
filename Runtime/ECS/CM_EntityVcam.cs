using System.Collections.Generic;
using Unity.Entities;
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

        // GML hack until I think of something better
        static Dictionary<Entity, CM_EntityVcam> sVcamCache = new Dictionary<Entity, CM_EntityVcam>();
        public static CM_EntityVcam GetEntityVcam(Entity e)
        {
            if (sVcamCache == null)
                sVcamCache = new Dictionary<Entity, CM_EntityVcam>();
            CM_EntityVcam vcam = null;
            if (!sVcamCache.TryGetValue(e, out vcam))
                sVcamCache[e] = vcam = new CM_EntityVcam(e);
            return vcam;
        }

        public static CameraState StateFromEntity(Entity e)
        {
            CameraState state = CameraState.Default;
            var m = World.Active.GetExistingManager<EntityManager>();
            if (m != null)
            {
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
                        LensShift = c.lensShift
                    };
                    noLens = false;
                }
                if (m.HasComponent<CM_VcamPosition>(e))
                {
                    var c = m.GetComponentData<CM_VcamPosition>(e);
                    state.RawPosition = c.raw;
                    state.ReferenceUp = c.up;
                    state.PositionCorrection = c.correction;
                }
                if (m.HasComponent<CM_VcamRotation>(e))
                {
                    var c = m.GetComponentData<CM_VcamRotation>(e);
                    state.ReferenceLookAt = c.lookAtPoint;
                    state.RawOrientation = c.raw;
                    state.OrientationCorrection = c.correction;
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
    }
}
