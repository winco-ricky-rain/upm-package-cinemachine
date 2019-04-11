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
        public CM_InputAxisDriver horizontalInput;
        public CM_InputAxisDriver verticalInput;
        public CM_InputAxisDriver radialInput;

        void Reset()
        {
            horizontalInput = new CM_InputAxisDriver
            {
                maxSpeed = 300,
                accelTime = 0.1f,
                decelTime = 0.1f,
                name = "Mouse X",
                invertInput = true
            };
            verticalInput = new CM_InputAxisDriver
            {
                maxSpeed = 4,
                accelTime = 0.1f,
                decelTime = 0.1f,
                name = "Mouse Y",
                invertInput = true
            };
            radialInput = new CM_InputAxisDriver
            {
                maxSpeed = 40,
                accelTime = 0.1f,
                decelTime = 0.1f,
                name = "Mouse ScrollWheel",
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
                horizontalInput.Update(Time.deltaTime, ref orbital.horizontalAxis);
                verticalInput.Update(Time.deltaTime, ref orbital.verticalAxis);
                radialInput.Update(Time.deltaTime, ref orbital.radialAxis);

                m.SetComponentData(e, orbital);
            }
        }
    }
}
