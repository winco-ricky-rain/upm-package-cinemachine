using UnityEngine;

namespace Cinemachine
{
    /// <summary>
    /// Property applied to AxisState.  Used for custom drawing in the inspector.
    /// </summary>
    public sealed class AxisStatePropertyAttribute : PropertyAttribute {}

    /// <summary>
    /// Property applied to OrbitalTransposer.Heading.  Used for custom drawing in the inspector.
    /// </summary>
    public sealed class OrbitalTransposerHeadingPropertyAttribute : PropertyAttribute {}

    /// <summary>
    /// Property applied to LensSettings.  Used for custom drawing in the inspector.
    /// </summary>
    public sealed class LensSettingsPropertyAttribute : PropertyAttribute {}

    /// <summary>
    /// Property applied to CinemachineBlendDefinition.  Used for custom drawing in the inspector.
    /// </summary>
    public sealed class CinemachineBlendDefinitionPropertyAttribute : PropertyAttribute {}

    /// <summary>Property field is a Tag.</summary>
    public sealed class TagFieldAttribute : PropertyAttribute {}

    /// <summary>Property field is a NoiseSettings asset.</summary>
    public sealed class NoiseSettingsPropertyAttribute : PropertyAttribute {}

    /// <summary>
    /// Used for custom drawing in the inspector.
    /// </summary>
    public sealed class CinemachineEmbeddedAssetPropertyAttribute : PropertyAttribute
    {
        public bool WarnIfNull;
        public CinemachineEmbeddedAssetPropertyAttribute(bool warnIfNull = false)
        {
            WarnIfNull = warnIfNull;
        }
    }

    /// <summary>
    /// Atrtribute to control the automatic generation of documentation.
    /// </summary>
    [DocumentationSorting(DocumentationSortingAttribute.Level.Undoc)]
    public sealed class DocumentationSortingAttribute : System.Attribute
    {
        /// <summary>Refinement level of the documentation</summary>
        public enum Level
        {
            /// <summary>Type is excluded from documentation</summary>
            Undoc,
            /// <summary>Type is documented in the API reference</summary>
            API,
            /// <summary>Type is documented in the highly-refined User Manual</summary>
            UserRef
        };
        /// <summary>Refinement level of the documentation.  The more refined, the more is excluded.</summary>
        public Level Category { get; private set; }

        /// <summary>Contructor with specific values</summary>
        public DocumentationSortingAttribute(Level category)
        {
            Category = category;
        }
    }
}
