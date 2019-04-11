using UnityEngine;
using System;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace Cinemachine.ECS
{
    /// <summary>
    /// Defines how to react to player input.
    /// The settings here control the responsiveness of the axis to player input.
    /// </summary>
    [Serializable]
    public struct CM_InputAxis
    {
        /// <summary>The current value of the axis.  You can drive this directly from a custom
        /// input system, or you can have it driven automatically by providing a valid input name
        /// and have the value driven by the internal Input Manager</summary>
        [NoSaveDuringPlay]
        [Tooltip("The current value of the axis.  You can drive this directly from a custom"
            + "input system, or you can have it driven automatically by providing a valid input name"
            + "and have the value driven by the internal Input Manager")]
        public float value;

        /// <summary>The valid range for the axis value</summary>
        [Tooltip("The valid range for the axis value")]
        [CM_RangeProperty]
        public float2 range;

        /// <summary>If checked, then the axis will wrap around at the min/max values, forming a loop</summary>
        [Tooltip("If checked, then the axis will wrap around at the min/max values, forming a loop")]
        public bool wrap;

        /// <summary>Helper for automatic axis recentering</summary>
        [Serializable]
        public struct Recentering
        {
            /// <summary>If checked, will enable automatic recentering of the
            /// axis. If FALSE, recenting is disabled.</summary>
            [Tooltip("If checked, will enable automatic recentering of the axis. If unchecked, "
                + "recenting is disabled.")]
            public bool enabled;

            /// <summary>The value to which recentring will bring the axis</summary>
            [Tooltip("The value to which recentring will bring the axis.")]
            public float center;

            /// <summary>If no input has been detected, the camera will wait
            /// this long in seconds before moving its heading to the default heading.</summary>
            [Tooltip("If no user input has been detected on the axis, the axis will wait this "
                + "long in seconds before recentering.")]
            public float wait;

            /// <summary>How long it takes to reach destination once recentering has started</summary>
            [Tooltip("How long it takes to reach destination once recentering has started.")]
            public float time;
        }

        /// <summary>Automatic recentering.  Valid only if HasRecentering is true</summary>
        [Tooltip("Automatic recentering to at-rest position")]
        public Recentering recentering;

        /// <summary>Value range is locked, i.e. not adjustable by the user (used by editor)</summary>
        public bool rangeLocked { get; set; }

        /// <summary>Clamp the value to range, taking wrap into account</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetClampedValue()
        {
            float r = range.y - range.x;
            float v = (value - range.x) % r;
            v += math.select(0, r, v < 0);
            v += range.x;
            v = math.select(value, v, wrap && r > MathHelpers.Epsilon);
            return math.clamp(v, range.x, range.y);
        }

        /// <summary>Clamp and scale the value to range 0...1, taking wrap into account</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetNormalizedValue()
        {
            float r = range.y - range.x;
            float v = GetClampedValue();
            return (v - range.x) / math.select(1, r, r > MathHelpers.Epsilon);
        }

        /// <summary>Call from OnValidate: Make sure the fields are sensible</summary>
        public void Validate()
        {
            range.y = math.max(range.x, range.y);
            recentering.wait = Mathf.Max(0, recentering.wait);
            recentering.time = Mathf.Max(0, recentering.time);
            recentering.center = math.clamp(recentering.center, range.x, range.y);
        }

        // Internal state
        float mLastAxisChangeTime;
        float mLastAxisValue;

        /// <summary>Cancel any recenetering in progress.</summary>
        public void CancelRecentering()
        {
            mLastAxisChangeTime = Time.time;
            mLastAxisValue = value;
        }

        /// <summary>Skip the wait time and start recentering now (only if enabled).</summary>
        public void RecenterNow()
        {
            mLastAxisChangeTime = 0;
            mLastAxisValue = value;
        }

        /// <summary>Bring the axis back to the cenetered state (only if enabled).</summary>
        /// <returns>True if the axis value was changed</returns>
        public bool DoRecentering(float deltaTime, float timeNow)
        {
            float v = GetClampedValue();
            float delta = recentering.center - v;
            if (!recentering.enabled || delta == 0)
                return false;

            if (deltaTime < 0)
            {
                value = recentering.center;
                mLastAxisChangeTime = timeNow;
                mLastAxisValue = value;
                return delta != 0;
            }

            if (v != mLastAxisValue)
            {
                // Cancel recentering
                mLastAxisChangeTime = timeNow;
                mLastAxisValue = v;
                return false;
            }

            if (timeNow < (mLastAxisChangeTime + recentering.wait))
                return false;

            // Determine the direction
            float target = recentering.center;
            float r = range.y - range.x;
            v += math.select(
                0, math.select(r, -r, v > target),
                wrap && math.abs(delta) > r * 0.5f);

            // Damp our way there
            v += MathHelpers.Damp(target - v, recentering.time, deltaTime);
            value = v;
            mLastAxisValue = GetClampedValue();
            return true;
        }
    }
}
