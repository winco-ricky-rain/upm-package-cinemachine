using UnityEngine; // For PropertyAttribute
using Cinemachine;

namespace Unity.Cinemachine3
{
    public sealed class CM_PipelineAttribute : System.Attribute
    {
        public CinemachineCore.Stage Stage { get; private set; }
        public CM_PipelineAttribute(CinemachineCore.Stage stage) { Stage = stage; }
    }

    /// <summary>
    /// Property applied to float2 to treat (x, y) as (min, max).
    /// Used for custom drawing in the inspector.
    /// </summary>
    public sealed class CM_Float2AsRangePropertyAttribute : PropertyAttribute {}

    /// <summary>
    /// Property applied to BlendCurve.  Used for custom drawing in the inspector.
    /// </summary>
    public sealed class BlendCurvePropertyAttribute : PropertyAttribute {}
}
