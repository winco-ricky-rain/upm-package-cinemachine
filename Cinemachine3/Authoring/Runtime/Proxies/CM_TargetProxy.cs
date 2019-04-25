using Unity.Mathematics;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
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
