using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Cinemachine;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [CM_Pipeline(CinemachineCore.Stage.Body)]
    [SaveDuringPlay]
    [ExecuteAlways]
    public class CM_VcamTrackedDollyProxy : CM_VcamComponentProxyBase<CM_VcamTrackedDolly>
    {
        /// <summary>The path to which the camera will be constrained.  This must be non-null.</summary>
        [Tooltip("The path to which the camera will be constrained.  This must be non-null.")]
        public CM_PathProxy path;

        public Entity PathEntity
        {
            get
            {
                // GML is there a better way?
                if (path != null)
                    return path.GetComponent<GameObjectEntity>().Entity;
                return Entity.Null;
            }
        }

        private void OnValidate()
        {
            var v = Value;
            v.damping = math.max(0, v.damping);
            v.angularDamping = math.max(0, v.angularDamping);
            var a = v.autoDolly;
                a.searchRadius = math.max(0, a.searchRadius);
                a.searchResolution = math.max(1, a.searchResolution);
                v.autoDolly = a;
            Value = v;
        }

        private void Reset()
        {
            Value = new CM_VcamTrackedDolly
            {
                positionUnits = CM_PathSystem.PositionUnits.Distance,
                damping = new float3(0, 0, 1),
                cameraUp = CM_VcamTrackedDolly.CameraUpMode.Default,
                autoDolly = new CM_VcamTrackedDolly.AutoDolly
                {
                    searchRadius = 2,
                    searchResolution = 5
                }
            };
        }

        void Update()
        {
            var v = Value;
            v.path = PathEntity;
            Value = v;
        }
    }
}
