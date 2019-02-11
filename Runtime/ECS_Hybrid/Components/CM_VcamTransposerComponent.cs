using Cinemachine.ECS;
using Unity.Mathematics;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [CM_Pipeline(CinemachineCore.Stage.Body)]
    [SaveDuringPlay]
    public class CM_VcamTransposerComponent : CM_VcamComponentBase<CM_VcamTransposer>
    {
        private void OnValidate()
        {
            var v = Value;
            v.damping = math.max(float3.zero, v.damping);
            v.angularDamping = math.max(0, v.angularDamping);
            Value = v;
        }

        private void Reset()
        {
            Value = new CM_VcamTransposer
            {
                bindingMode = CM_VcamTransposer.BindingMode.LockToTargetWithWorldUp,
                followOffset = new float3(0, 0, -10f),
                damping = new float3(1, 1, 1),
                angularDamping = 1
            };
        }
    }
}
