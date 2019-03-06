using Unity.Entities;
using Cinemachine.ECS;
using Unity.Mathematics;

namespace Cinemachine.ECS_Hybrid
{
    [UnityEngine.DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_ChannelComponent : ComponentDataProxy<CM_Channel>
    {
        private void OnValidate()
        {
            var v = Value;
            v.settings.worldOrientation = math.normalizesafe(v.settings.worldOrientation);
            Value = v;
        }

        private void Reset()
        {
            Value = new CM_Channel
            {
                settings = new CM_Channel.Settings { worldOrientation = quaternion.identity },
                defaultBlend = new CinemachineBlendDefinition
                {
                    m_Style = CinemachineBlendDefinition.Style.EaseInOut,
                    m_Time = 2f
                }
            };
        }
    }
}
