using Unity.Entities;
using Cinemachine.ECS;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_VcamBlendHintProxy : CM_ComponentProxyBase<CM_VcamBlendHint> { }
}
