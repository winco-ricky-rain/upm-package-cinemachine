using System;
using UnityEngine;

namespace Unity.Cinemachine.Common
{
    /// <summary>Definition of a Camera blend.  This struct holds the information
    /// necessary to generate a suitable AnimationCurve for a Cinemachine Blend.</summary>
    [Serializable]
    public struct CinemachineBlendDefinition
    {
        /// <summary>Supported predefined shapes for the blend curve.</summary>
        public enum Style
        {
            /// <summary>Zero-length blend</summary>
            Cut,
            /// <summary>S-shaped curve, giving a gentle and smooth transition</summary>
            EaseInOut,
            /// <summary>Linear out of the outgoing shot, and easy into the incoming</summary>
            EaseIn,
            /// <summary>Easy out of the outgoing shot, and linear into the incoming</summary>
            EaseOut,
            /// <summary>Easy out of the outgoing, and hard into the incoming</summary>
            HardIn,
            /// <summary>Hard out of the outgoing, and easy into the incoming</summary>
            HardOut,
            /// <summary>Linear blend.  Mechanical-looking.</summary>
            Linear,
            /// <summary>Custom blend curve.</summary>
            Custom
        };

        /// <summary>The shape of the blend curve.</summary>
        [Tooltip("Shape of the blend curve")]
        public Style m_Style;

        /// <summary>The duration (in seconds) of the blend</summary>
        [Tooltip("Duration of the blend, in seconds")]
        public float m_Time;

        /// <summary>
        /// A user-defined AnimationCurve, used only if style is Custom.
        /// Curve MUST be normalized, i.e. time range [0...1], value range [0...1].
        /// </summary>
        [BlendCurveProperty]
        public BlendCurve m_CustomCurve;

        /// <summary>
        /// A normalized AnimationCurve specifying the interpolation curve
        /// for this camera blend. Y-axis values must be in range [0,1] (internally clamped
        /// within Blender) and time must be in range of [0, 1].
        /// </summary>
        public BlendCurve BlendCurve
        {
            get
            {
                switch (m_Style)
                {
                    case Style.Cut: return BlendCurve.Linear;
                    case Style.EaseInOut: return BlendCurve.Default;
                    case Style.EaseIn: return new BlendCurve { A = 0.5f, B = 0, bias = 0 };
                    case Style.EaseOut: return new BlendCurve { A = 0, B = 0.5f, bias = 0 };
                    case Style.HardIn: return new BlendCurve { A = 0, B = 1, bias = 0 };
                    case Style.HardOut: return new BlendCurve { A = 1, B = 0, bias = 0 };
                    case Style.Linear: return BlendCurve.Linear;
                    default: break;
                }
                return m_CustomCurve;
            }
        }
    }
}
