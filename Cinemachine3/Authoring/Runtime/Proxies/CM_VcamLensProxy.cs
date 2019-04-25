using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [UnityEngine.DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_VcamLensProxy : CM_ComponentProxyBase<CM_VcamLens>
    {
        private void Reset()
        {
            Value = CM_VcamLens.Default;
        }
    }
}
