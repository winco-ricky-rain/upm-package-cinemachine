using UnityEngine;
using Unity.Entities;

namespace Unity.Cinemachine3.Authoring
{
    /// <summary>
    /// This is a very simple behaviour that constrains its transform to a CM_Path.
    /// It can be used to animate any objects along a path, or as a Follow target for
    /// Cinemachine Virtual Cameras.
    /// </summary>
    [ExecuteAlways]
    public class CM_DollyCart : MonoBehaviour
    {
        /// <summary>The path to follow</summary>
        [Tooltip("The path to follow")]
        public CM_PathProxy path;

        /// <summary>This enum defines the options available for the update method.</summary>
        public enum UpdateMethod
        {
            /// <summary>Updated in normal MonoBehaviour Update.</summary>
            Update,
            /// <summary>Updated in sync with the Physics module, in FixedUpdate</summary>
            FixedUpdate,
            /// <summary>Updated in normal MonoBehaviour LateUpdate</summary>
            LateUpdate
        };

        /// <summary>When to move the cart, if Velocity is non-zero</summary>
        [Tooltip("When to move the cart, if Velocity is non-zero")]
        public UpdateMethod updateMethod = UpdateMethod.Update;

        /// <summary>How to interpret the Path Position</summary>
        [Tooltip("How to interpret the Path Position.  If set to Path Units, values are as follows: "
            + "0 represents the first waypoint on the path, 1 is the second, and so on.  "
            + "Values in-between are points on the path in between the waypoints.  "
            + "If set to Distance, then Path Position represents distance along the path.")]
        public CM_PathSystem.PositionUnits positionUnits = CM_PathSystem.PositionUnits.Distance;

        /// <summary>The cart's current position on the path, in distance units</summary>
        [Tooltip("The position along the path at which the cart will be placed.  "
            + "This can be animated directly or, if the velocity is non-zero, will be updated "
            + "automatically.  The value is interpreted according to the Position Units setting.")]
        public float position;

        /// <summary>Move the cart with this speed</summary>
        [Tooltip("Move the cart with this speed along the path.  The value is interpreted according "
            + "to the Position Units setting.")]
        public float speed;

        void FixedUpdate()
        {
            if (updateMethod == UpdateMethod.FixedUpdate)
                SetCartPosition(position + speed * Time.deltaTime);
        }

        void Update()
        {
            float s = Application.isPlaying ? speed : 0;
            if (updateMethod == UpdateMethod.Update)
                SetCartPosition(position + s * Time.deltaTime);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
                SetCartPosition(position);
            else if (updateMethod == UpdateMethod.LateUpdate)
                SetCartPosition(position + speed * Time.deltaTime);
        }

        void SetCartPosition(float distanceAlongPath)
        {
            if (path == null)
                return;
            var pathSystem = World.Active?.GetExistingSystem<CM_PathSystem>();
            if (pathSystem == null)
                return;
            var e = path.Entity;
            if (e == Entity.Null)
                return;
            position = pathSystem.ClampUnit(e, distanceAlongPath, positionUnits);
            transform.position = pathSystem.EvaluatePositionAtUnit(e, position, positionUnits);
            transform.rotation = pathSystem.EvaluateOrientationAtUnit(e, position, positionUnits);
        }
    }
}
