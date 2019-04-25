using Unity.Entities;
using Unity.Mathematics;
using Cinemachine;

namespace Unity.Cinemachine3.Authoring
{
    [UnityEngine.DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_ChannelProxy : ComponentDataProxy<CM_Channel>
    {
        private void OnValidate()
        {
            var v = Value;
            v.settings.worldOrientation = math.normalizesafe(v.settings.worldOrientation);
            v.activateAfter = math.max(0, v.activateAfter);
            v.minDuration = math.max(0, v.minDuration);
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
