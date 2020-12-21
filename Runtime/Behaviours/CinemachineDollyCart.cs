using UnityEngine;
using UnityEngine.Serialization;

namespace Cinemachine
{
    /// <summary>
    /// This is a very simple behaviour that constrains its transform to a CinemachinePath.  
    /// It can be used to animate any objects along a path, or as a Follow target for 
    /// Cinemachine Virtual Cameras.
    /// </summary>
    [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
#if UNITY_2018_3_OR_NEWER
    [ExecuteAlways]
#else
    [ExecuteInEditMode]
#endif
    public class CinemachineDollyCart : MonoBehaviour
    {
        /// <summary>The path to follow</summary>
        [Tooltip("The path to follow")]
        public CinemachinePathBase m_Path;

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
        public UpdateMethod m_UpdateMethod = UpdateMethod.Update;

        /// <summary>How to interpret the Path Position</summary>
        [Tooltip("How to interpret the Path Position.  If set to Path Units, values are as follows: 0 represents the first waypoint on the path, 1 is the second, and so on.  Values in-between are points on the path in between the waypoints.  If set to Distance, then Path Position represents distance along the path.")]
        public CinemachinePathBase.PositionUnits m_PositionUnits = CinemachinePathBase.PositionUnits.Distance;

        /// <summary>Move the cart with this speed</summary>
        [Tooltip("Move the cart with this speed along the path.  The value is interpreted according to the Position Units setting.")]
        [FormerlySerializedAs("m_Velocity")]
        public float m_Speed;

        /// <summary>The cart's current position on the path, in distance units</summary>
        [Tooltip("The position along the path at which the cart will be placed.  This can be animated directly or, if the velocity is non-zero, will be updated automatically.  The value is interpreted according to the Position Units setting.")]
        [FormerlySerializedAs("m_CurrentDistance")]
        public float m_Position;

        void FixedUpdate()
        {
            if (m_UpdateMethod == UpdateMethod.FixedUpdate)
                SetCartPosition(m_Position + m_Speed * Time.deltaTime);
        }

        void Update()
        {
            float speed = Application.isPlaying ? m_Speed : 0;
            if (m_UpdateMethod == UpdateMethod.Update)
                SetCartPosition(m_Position + speed * Time.deltaTime);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
                SetCartPosition(m_Position);
            else if (m_UpdateMethod == UpdateMethod.LateUpdate)
                SetCartPosition(m_Position + m_Speed * Time.deltaTime);
        }

        void SetCartPosition(float distanceAlongPath)
        {
            if (m_Path != null)
            {
                m_Position = m_Path.StandardizeUnit(distanceAlongPath, m_PositionUnits);
                transform.position = m_Path.EvaluatePositionAtUnit(m_Position, m_PositionUnits);
                transform.rotation = m_Path.EvaluateOrientationAtUnit(m_Position, m_PositionUnits);
            }
        }
    }
}

/*Turn this window off by removing the example window script on Main Camera

Scene setup:
<i>Vehicle</i> follows the path driven by its <i>CinemachineDollyCart</i> component.
<i>CM vcam follow camera</i> looks at and follows object <i>Vehicle</i> and has Aim and Body compoents setup to illustrate the main controls:

Body
Follow offset - where the camera is in relation to the follow object
Damping - positional lag between the camera and follow object
Binding mode - different styles of relationships between camera and follow object
Pitch, Yaw, Roll damping - rotation damping on the axis binding 

Aim
Tracked offset - looking ahead of the ship
Damping - letting the ship move around on the screen, lots of vertical, a little horizontal.*/