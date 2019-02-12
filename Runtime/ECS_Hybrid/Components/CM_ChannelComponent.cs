using Unity.Entities;
using Cinemachine.ECS;
using Unity.Mathematics;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_ChannelComponent : ComponentDataWrapper<CM_Channel>
    {
        private void OnValidate()
        {
            var v = Value;
            v.worldOrientationOverride = math.normalizesafe(v.worldOrientationOverride);
            Value = v;
        }

        private void Reset()
        {
            Value = new CM_Channel
            {
                worldOrientationOverride = quaternion.identity,
                defaultBlend = new CinemachineBlendDefinition
                {
                    m_Style = CinemachineBlendDefinition.Style.EaseInOut,
                    m_Time = 2f
                }
            };
        }
    }
}
