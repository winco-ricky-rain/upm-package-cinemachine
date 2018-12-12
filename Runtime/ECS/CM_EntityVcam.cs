using Unity.Entities;
using UnityEngine;

namespace Cinemachine.ECS
{
    public class CM_EntityVcam : ICinemachineCamera
    {
        public Entity entity;

        public CM_EntityVcam(Entity e) { entity = e; }

        public string Name { get { return entity.ToString(); } }
        public string Description { get { return ""; }}
        public CameraState State { get { return StateFromEntity(entity); } }

        public bool IsValid { get { return entity != Entity.Null; } }
        public Entity Entity { get { return entity; } }

        public ICinemachineCamera ParentCamera { get { return null; } }
        public bool IsLiveChild(ICinemachineCamera vcam) { return false; }

        public void UpdateCameraState(Vector3 worldUp, float deltaTime) {}
        public void InternalUpdateCameraState(Vector3 worldUp, float deltaTime) {}
        public void OnTransitionFromCamera(ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime) {}
        public void OnTargetObjectWarped(Transform target, Vector3 positionDelta) {}

        public static CameraState StateFromEntity(Entity e)
        {
            CameraState state = CameraState.Default;
            var m = World.Active.GetExistingManager<EntityManager>();
            if (m != null)
            {
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
                }
                if (m.HasComponent<CM_VcamPosition>(e))
                {
                    var c = m.GetComponentData<CM_VcamPosition>(e);
                    state.RawPosition = c.raw;
                    state.ReferenceUp = c.up;
                }
                if (m.HasComponent<CM_VcamPositionCorrection>(e))
                {
                    var c = m.GetComponentData<CM_VcamPositionCorrection>(e);
                    state.PositionCorrection = c.value;
                }
                if (m.HasComponent<CM_VcamRotation>(e))
                {
                    var c = m.GetComponentData<CM_VcamRotation>(e);
                    state.ReferenceLookAt = c.lookAtPoint;
                    state.RawOrientation = c.raw;
                }
                if (m.HasComponent<CM_VcamRotationCorrection>(e))
                {
                    var c = m.GetComponentData<CM_VcamRotationCorrection>(e);
                    state.OrientationCorrection = c.value;
                }
                if (m.HasComponent<CM_VcamBlendHint>(e))
                {
                    var c = m.GetComponentData<CM_VcamBlendHint>(e);
                    state.BlendHint = (CameraState.BlendHintValue)c.blendHint; // GML fixme
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
