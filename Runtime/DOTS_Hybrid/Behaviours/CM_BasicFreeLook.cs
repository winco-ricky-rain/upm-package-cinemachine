using UnityEngine;
using Cinemachine.ECS;
using Unity.Entities;

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
        public CM_InputAxisDriver horizontalAxis;
        public CM_InputAxisDriver verticalAxis;
        public CM_InputAxisDriver radialAxis;

        void Reset()
        {
            horizontalAxis = new CM_InputAxisDriver
            {
                maxSpeed = 300,
                accelTime = 0.1f,
                decelTime = 0.1f,
                inputName = "Mouse X",
                invertInput = true
            };
            verticalAxis = new CM_InputAxisDriver
            {
                maxSpeed = 4,
                accelTime = 0.1f,
                decelTime = 0.1f,
                inputName = "Mouse Y",
                invertInput = true
            };
            radialAxis = new CM_InputAxisDriver
            {
                maxSpeed = 40,
                accelTime = 0.1f,
                decelTime = 0.1f,
                inputName = "Mouse ScrollWheel",
            };
        }

        protected override void Update()
        {
            base.Update();

            var e = Entity;
            var m = World.Active?.EntityManager;
            if (m != null && m.Exists(e) && m.HasComponent<CM_VcamOrbital>(e))
            {
                CM_VcamOrbital orbital = m.GetComponentData<CM_VcamOrbital>(e);

                // Update our axes
                horizontalAxis.Update(Time.deltaTime, ref orbital.horizontalAxis);
                verticalAxis.Update(Time.deltaTime, ref orbital.verticalAxis);
                radialAxis.Update(Time.deltaTime, ref orbital.radialAxis);

                m.SetComponentData(e, orbital);
            }
        }
    }
}
