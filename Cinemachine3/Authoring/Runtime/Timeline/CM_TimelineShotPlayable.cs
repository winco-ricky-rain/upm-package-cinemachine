#if CINEMACHINE_TIMELINE

using UnityEngine.Playables;
using Cinemachine.ECS_Hybrid;

//namespace Cinemachine.ECS_Hybrid
//{
    internal sealed class CM_TimelineShotPlayable : PlayableBehaviour
    {
        public CM_VcamBase VirtualCamera;
        public bool IsValid { get { return VirtualCamera != null; } }
    }
//}
#endif
