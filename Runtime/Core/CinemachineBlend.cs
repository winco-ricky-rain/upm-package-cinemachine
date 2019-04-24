using Cinemachine.Utility;
using System;
using UnityEngine;
using Unity.Entities;

namespace Cinemachine
{
    /// <summary>
    /// Copied from CM3
    /// Property applied to BlendCurve.  Used for custom drawing in the inspector.
    /// </summary>
    public sealed class BlendCurvePropertyAttribute : PropertyAttribute {}

    [Serializable]
    public struct BlendCurve
    {
        [Range(0, 1)]
        public float A;

        [Range(0, 1)]
        public float B;

        [Range(-1, 1)]
        public float bias;

        public float Evaluate(float t)
        {
            t = MathHelpers.Bias(t, (1f - bias) * 0.5f);
            return MathHelpers.Bezier(t, 0, A, 1 - B, 1);
        }

        public static BlendCurve Default { get { return new BlendCurve{ A = 0, B = 0, bias = 0 }; } }
        public static BlendCurve Linear { get { return new BlendCurve{ A = 0.3333f, B = 0.3333f, bias = 0 }; } }
    }

    /// <summary>
    /// Describes a blend between 2 Cinemachine Virtual Cameras, and holds the
    /// current state of the blend.
    /// </summary>
    public class CinemachineBlend
    {
        /// <summary>First camera in the blend</summary>
        public ICinemachineCamera CamA { get; set; }

        /// <summary>Second camera in the blend</summary>
        public ICinemachineCamera CamB { get; set; }

        /// <summary>The curve that describes the way the blend transitions over time
        /// from the first camera to the second.  X-axis is normalized time (0...1) over which
        /// the blend takes place and Y axis is blend weight (0..1)</summary>
        public BlendCurve BlendCurve { get; set; }

        /// <summary>The current time relative to the start of the blend</summary>
        public float TimeInBlend { get; set; }

        /// <summary>The current weight of the blend.  This is an evaluation of the
        /// BlendCurve at the current time relative to the start of the blend.
        /// 0 means camA, 1 means camB.</summary>
        public float BlendWeight
        {
            get { return IsComplete ? 1 : BlendCurve.Evaluate(TimeInBlend / Duration); }
        }

        /// <summary>Validity test for the blend.  True if either camera is defined.</summary>
        public bool IsValid { get { return ((CamA != null && CamA.IsValid) || (CamB != null && CamB.IsValid)); } }

        /// <summary>Duration in seconds of the blend.</summary>
        public float Duration { get; set; }

        /// <summary>True if the time relative to the start of the blend is greater
        /// than or equal to the blend duration</summary>
        public bool IsComplete { get { return TimeInBlend >= Duration || !IsValid; } }

        /// <summary>Does the blend use a specific Cinemachine Virtual Camera?</summary>
        /// <param name="cam">The camera to test</param>
        /// <returns>True if the camera is involved in the blend</returns>
        public bool Uses(ICinemachineCamera cam)
        {
            if (cam == CamA || cam == CamB)
                return true;
            BlendSourceVirtualCamera b = CamA as BlendSourceVirtualCamera;
            if (b != null && b.Blend.Uses(cam))
                return true;
            b = CamB as BlendSourceVirtualCamera;
            if (b != null && b.Blend.Uses(cam))
                return true;
            return false;
        }

        /// <summary>Construct a blend</summary>
        /// <param name="a">First camera</param>
        /// <param name="b">Second camera</param>
        /// <param name="curve">Blend curve</param>
        /// <param name="duration">Duration of the blend, in seconds</param>
        /// <param name="t">Current time in blend, relative to the start of the blend</param>
        public CinemachineBlend(
            ICinemachineCamera a, ICinemachineCamera b, BlendCurve curve, float duration, float t)
        {
            CamA = a;
            CamB = b;
            BlendCurve = curve;
            TimeInBlend = t;
            Duration = duration;
        }

        /// <summary>Make sure the source cameras get updated.</summary>
        /// <param name="worldUp">Default world up.  Individual vcams may modify this</param>
        /// <param name="deltaTime">Time increment used for calculating time-based behaviours (e.g. damping)</param>
        public void UpdateCameraState(Vector3 worldUp, float deltaTime)
        {
            // Make sure both cameras have been updated (they are not necessarily
            // enabled, and only enabled cameras get updated automatically
            // every frame)
            if (CamA != null && CamA.IsValid)
                CamA.UpdateCameraState(worldUp, deltaTime);
            if (CamB != null && CamB.IsValid)
                CamB.UpdateCameraState(worldUp, deltaTime);
        }

        /// <summary>Compute the blended CameraState for the current time in the blend.</summary>
        public CameraState State
        {
            get
            {
                if (CamA == null || !CamA.IsValid)
                {
                    if (CamB == null || !CamB.IsValid)
                        return CameraState.Default;
                    return CamB.State;
                }
                if (CamB == null || !CamB.IsValid)
                    return CamA.State;
                return CameraState.Lerp(CamA.State, CamB.State, BlendWeight);
            }
        }

        public ICinemachineCamera DeepCamB()
        {
            ICinemachineCamera vcam = CamB;
            while (vcam != null)
            {
                if (!vcam.IsValid)
                    return null;    // deleted!
                BlendSourceVirtualCamera bs = vcam as BlendSourceVirtualCamera;
                if (bs == null)
                    break;
                vcam = bs.Blend.CamB;
            }
            return vcam;
        }
    }

    /// <summary>Definition of a Camera blend.  This struct holds the information
    /// necessary to generate a suitable AnimationCurve for a Cinemachine Blend.</summary>
    [Serializable]
    [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
    public struct CinemachineBlendDefinition
    {
        /// <summary>Supported predefined shapes for the blend curve.</summary>
        [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
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

    /// <summary>
    /// Point source for blending. It's not really a virtual camera, but takes
    /// a CameraState and exposes it as a virtual camera for the purposes of blending.
    /// </summary>
    internal class StaticPointVirtualCamera : ICinemachineCamera
    {
        public StaticPointVirtualCamera(CameraState state, string name) { State = state; Name = name; }
        public void SetState(CameraState state) { State = state; }

        public string Name { get; private set; }
        public string Description { get { return ""; }}
        public CameraState State { get; private set; }
        public bool IsValid { get { return true; } }
        public ICinemachineCamera ParentCamera { get { return null; } }
        public bool IsLiveChild(ICinemachineCamera vcam) { return false; }
        public void UpdateCameraState(Vector3 worldUp, float deltaTime) {}
        public void InternalUpdateCameraState(Vector3 worldUp, float deltaTime) {}
        public void OnTransitionFromCamera(ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime) {}
        public void OnTargetObjectWarped(Transform target, Vector3 positionDelta) {}
        public bool IsLive { get { return false; }}
        public Entity AsEntity { get { return Entity.Null; }}
    }

    /// <summary>
    /// Blend result source for blending.   This exposes a CinemachineBlend object
    /// as an ersatz virtual camera for the purposes of blending.  This achieves the purpose
    /// of blending the result oif a blend.
    /// </summary>
    internal class BlendSourceVirtualCamera : ICinemachineCamera
    {
        public BlendSourceVirtualCamera(CinemachineBlend blend) { Blend = blend; }
        public CinemachineBlend Blend { get; set; }

        public string Name { get { return "Mid-blend"; }}
        public string Description { get { return Blend == null ? "(null)" : Blend.Description(); }}
        public CameraState State { get; private set; }
        public bool IsValid { get { return Blend != null && Blend.IsValid; } }
        public ICinemachineCamera ParentCamera { get { return null; } }
        public bool IsLiveChild(ICinemachineCamera vcam) { return Blend != null && (vcam == Blend.CamA || vcam == Blend.CamB); }
        public CameraState CalculateNewState(float deltaTime) { return State; }
        public void UpdateCameraState(Vector3 worldUp, float deltaTime)
        {
            if (Blend != null)
            {
                Blend.UpdateCameraState(worldUp, deltaTime);
                State = Blend.State;
            }
        }
        public void InternalUpdateCameraState(Vector3 worldUp, float deltaTime) {}
        public void OnTransitionFromCamera(ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime) {}
        public void OnTargetObjectWarped(Transform target, Vector3 positionDelta) {}
        public bool IsLive { get { return false; }}
        public Entity AsEntity { get { return Entity.Null; }}
    }
}
