
namespace Cinemachine.ECS_Hybrid
{
    public sealed class CM_PipelineAttribute : System.Attribute
    {
        public CinemachineCore.Stage Stage { get; private set; }
        public CM_PipelineAttribute(CinemachineCore.Stage stage) { Stage = stage; }
    }
}
