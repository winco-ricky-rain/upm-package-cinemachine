using Unity.Entities;
using Cinemachine.ECS;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_VcamBlendHintComponent : CM_VcamComponentBase<CM_VcamBlendHint> { }
}
