using Unity.Entities;

namespace Cinemachine
{
    public interface ICinemachineBlendProvider
    {
        CinemachineBlendDefinition GetBlendForVirtualCameras(
            ICinemachineCamera fromCam, ICinemachineCamera toCam,
            CinemachineBlendDefinition defaultBlend);
    }

    // ECS version GML to be improved
    public interface ICinemachineEntityBlendProvider
    {
        CinemachineBlendDefinition GetBlendForVirtualCameras(
            Entity fromCam, Entity toCam,
            CinemachineBlendDefinition defaultBlend);
    }
}
