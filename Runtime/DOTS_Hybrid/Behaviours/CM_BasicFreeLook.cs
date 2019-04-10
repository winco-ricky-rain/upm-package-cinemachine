using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine;
using Cinemachine.ECS;
using System;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Cinemachine.ECS_Hybrid
{
    /// <summary>
    /// Simple FreeLook version of the virtual camera, just spline-driven orbital position
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [SaveDuringPlay]
    [AddComponentMenu("Cinemachine/CM_BasicFreeLook")]
    public class CM_BasicFreeLook : CM_Vcam
    {
        /// <summary>The Horizontal axis.  -180..180.  0 is the center.
        /// Rotates the camera horizontally around the target</summary>
        [Tooltip("The Horizontal axis.  Value is -180..180.  0 is the center.  "
            + "Rotates the camera horizontally around the target")]
        [AxisStateProperty]
        public AxisState horizontalAxis;
        /// <summary>The Vertical axis.  Value is -1..1.  Chooses how to blend the child rigs</summary>
        [Tooltip("The Vertical axis.  Value is -1..1.  0.5 is the middle rig.  "
            + "Chooses how to blend the child rigs")]
        [AxisStateProperty]
        public AxisState verticalAxis;

        [Tooltip("The Radial axis.  Scales the orbits.  Value is the base radius of the orbits")]
        [AxisStateProperty]
        public AxisState radialAxis;

        void Awake()
        {
            // GML todo: get rid of this hack
            horizontalAxis.HasRecentering = true;
            verticalAxis.HasRecentering = true;
            radialAxis.HasRecentering = true;
        }

        void Reset()
        {
            horizontalAxis = new AxisState(-180, 180, true, false, 300f, 0.1f, 0.1f, "Mouse X", true);
            verticalAxis = new AxisState(-1, 1, false, true, 2f, 0.2f, 0.1f, "Mouse Y", false);
            radialAxis = new AxisState(1, 1, false, false, 100, 0f, 0f, "Mouse ScrollWheel", false);
            horizontalAxis.HasRecentering = true;
            verticalAxis.HasRecentering = true;
            radialAxis.HasRecentering = true;
        }

        protected override void Update()
        {
            base.Update();

            bool hasOrbital = false;
            var e = Entity;
            var m = World.Active?.EntityManager;
            hasOrbital = m != null && m.Exists(e) && m.HasComponent<CM_VcamOrbital>(e);
            CM_VcamOrbital orbital = hasOrbital
                ? m.GetComponentData<CM_VcamOrbital>(e) : new CM_VcamOrbital();

            // Update our axes
            bool activeCam = IsLive;
            if (activeCam)
            {
                if (horizontalAxis.Update(Time.deltaTime))
                    horizontalAxis.m_Recentering.CancelRecentering();
                if (verticalAxis.Update(Time.deltaTime))
                    verticalAxis.m_Recentering.CancelRecentering();
                if (radialAxis.Update(Time.deltaTime))
                    radialAxis.m_Recentering.CancelRecentering();
            }
            float heading = horizontalAxis.Value;
            if (orbital.bindingMode == CM_VcamTransposerSystem.BindingMode.SimpleFollowWithWorldUp)
                horizontalAxis.Value = 0;
            else
            {
                horizontalAxis.m_Recentering.DoRecentering(ref horizontalAxis, Time.deltaTime, 0);
                heading = horizontalAxis.Value;
            }
            verticalAxis.m_Recentering.DoRecentering(ref verticalAxis, Time.deltaTime, 0);
            radialAxis.m_Recentering.DoRecentering(ref radialAxis, Time.deltaTime, 1);

            if (hasOrbital)
            {
                orbital.orbitPosition = new float3(heading, GetVerticalAxisValue(), radialAxis.Value);
                m.SetComponentData(e, orbital);
            }
        }

        private float GetVerticalAxisValue()
        {
            float range = verticalAxis.m_MaxValue - verticalAxis.m_MinValue;
            float v = verticalAxis.Value - verticalAxis.m_MinValue;
            return math.select(0, v / range, range > MathHelpers.Epsilon) * 2 - 1;
        }
    }
}
