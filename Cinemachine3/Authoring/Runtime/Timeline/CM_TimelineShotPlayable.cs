#if CINEMACHINE_TIMELINE

using UnityEngine.Playables;
using Unity.Cinemachine3.Authoring;

//namespace Unity.Cinemachine3.Authoring
//{
    internal sealed class CM_TimelineShotPlayable : PlayableBehaviour
    {
        public CM_VcamBase VirtualCamera;
        public bool IsValid { get { return VirtualCamera != null; } }
    }
//}
#endif
