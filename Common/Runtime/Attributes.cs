using UnityEngine;

namespace Unity.Cinemachine.Common
{
    /// <summary>
    /// A dummy class for dependency checking.  GML todo: replace with something else
    /// </summary>
    public sealed class AssemblyDependencyClass {};

    /// <summary>
    /// Copied from CM3
    /// Property applied to BlendCurve.  Used for custom drawing in the inspector.
    /// </summary>
    public sealed class BlendCurvePropertyAttribute : PropertyAttribute {}

    /// <summary>
    /// Invoke play-mode-save for a class.  This class's fields will be scanned
    /// upon exiting play mode, and its property values will be applied to the scene object.
    /// This is a stopgap measure that will become obsolete once Unity implements
    /// play-mode-save in a more general way.
    /// </summary>
    public sealed class SaveDuringPlayAttribute : System.Attribute {}

    /// <summary>
    /// Suppresses play-mode-save for a field.  Use it if the calsee has [SaveDuringPlay]
    /// attribute but there are fields in the class that shouldn't be saved.
    /// </summary>
    public sealed class NoSaveDuringPlayAttribute : PropertyAttribute {}

    /// <summary>
    /// Suppresses the foldout on a complex property
    /// </summary>
    public sealed class HideFoldoutAttribute : PropertyAttribute {}
}
