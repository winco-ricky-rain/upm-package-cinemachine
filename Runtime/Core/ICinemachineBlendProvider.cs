using Unity.Entities;

namespace Cinemachine
{
    public interface ICinemachineBlendProvider
    {
        CinemachineBlendDefinition GetBlendForVirtualCameras(
            ICinemachineCamera fromCam, ICinemachineCamera toCam,
            CinemachineBlendDefinition defaultBlend);
    }
}
