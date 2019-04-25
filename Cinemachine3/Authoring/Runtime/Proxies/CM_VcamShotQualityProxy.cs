namespace Unity.Cinemachine3.Authoring
{
    [UnityEngine.DisallowMultipleComponent]
    public class CM_VcamShotQualityProxy : CM_ComponentProxyBase<CM_VcamShotQuality>
    {
        private void Reset()
        {
            Value = new CM_VcamShotQuality
            {
                value = CM_VcamShotQuality.DefaultValue
            };
        }
    }
}
