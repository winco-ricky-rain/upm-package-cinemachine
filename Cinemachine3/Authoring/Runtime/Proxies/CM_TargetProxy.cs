using Cinemachine.ECS;
using Unity.Mathematics;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_TargetProxy : CM_ComponentProxyBase<CM_Target>
    {
        private void OnValidate()
        {
            var v = Value;
            v.radius = math.max(0, v.radius);
            Value = v;
        }
    }
}
