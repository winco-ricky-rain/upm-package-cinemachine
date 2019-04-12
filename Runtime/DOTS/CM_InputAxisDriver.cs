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
    public struct CM_InputAxisDriver
    {
        /// <summary>Multiply the input by this amount prior to processing.
        /// Controls the input power</summary>
        [Tooltip("Multiply the input by this amount prior to processing.  Controls the input power.")]
        public float multiplier;

        /// <summary>The amount of time in seconds it takes to accelerate to
        /// MaxSpeed with the supplied Axis at its maximum value</summary>
        [Tooltip("The amount of time in seconds it takes to accelerate to MaxSpeed with the "
            + "supplied Axis at its maximum value")]
        public float accelTime;

        /// <summary>The amount of time in seconds it takes to decelerate
        /// the axis to zero if the supplied axis is in a neutral position</summary>
        [Tooltip("The amount of time in seconds it takes to decelerate the axis to zero if "
            + "the supplied axis is in a neutral position")]
        public float decelTime;

        /// <summary>The name of this axis as specified in Unity Input manager.
        /// Setting to an empty string will disable the automatic updating of this axis</summary>
        [Tooltip("The name of this axis as specified in Unity Input manager. "
            + "Setting to an empty string will disable the automatic updating of this axis")]
        public string name;

        /// <summary>The value of the input axis.  A value of 0 means no input
        /// You can drive this directly from a
        /// custom input system, or you can set the Axis Name and have the value
        /// driven by the internal Input Manager</summary>
        [NoSaveDuringPlay]
        [Tooltip("The value of the input axis.  A value of 0 means no input.  You can drive "
            + "this directly from a custom input system, or you can set the Axis Name and "
            + "have the value driven by the internal Input Manager")]
        public float inputValue;

        // Internal state
        private float mCurrentSpeed;

        /// <summary>Call from OnValidate: Make sure the fields are sensible</summary>
        public void Validate()
        {
            multiplier = math.max(0, multiplier);
            accelTime = math.max(0, accelTime);
            decelTime = math.max(0, decelTime);
        }

        public void Reset()
        {
            inputValue = 0;
            mCurrentSpeed = 0;
        }

        /// <summary>
        /// Updates the state of this axis based on the axis defined
        /// by AxisState.m_AxisName
        /// </summary>
        /// <param name="deltaTime">Delta time in seconds</param>
        /// <returns>Returns <b>true</b> if this axis' input was non-zero this Update,
        /// <b>false</b> otherwise</returns>
        public bool Update(float deltaTime, ref CM_InputAxis axis)
        {
            if (!string.IsNullOrEmpty(name))
            {
                try { inputValue = CinemachineCore.GetInputAxis(name); }
                catch (ArgumentException) {}
                //catch (ArgumentException e) { Debug.LogError(e.ToString()); }
            }

            float input = inputValue * multiplier;
            if (deltaTime < MathHelpers.Epsilon)
                mCurrentSpeed = 0;
            else
            {
                float speed = input / deltaTime;
                float dampTime = math.select(
                    accelTime, decelTime, math.abs(speed) < math.abs(mCurrentSpeed));
                speed = mCurrentSpeed + MathHelpers.Damp(speed - mCurrentSpeed, dampTime, deltaTime);
                mCurrentSpeed = speed;

                // Decelerate to the end points of the range if not wrapping
                float range = axis.range.y - axis.range.x;
                if (!axis.wrap && decelTime > MathHelpers.Epsilon && range > MathHelpers.Epsilon)
                {
                    float v0 = axis.GetClampedValue();
                    float v = axis.ClampValue(v0 + speed * deltaTime);
                    float d = math.select(v - axis.range.x, axis.range.y - v, speed > 0);
                    if (d < (0.1f * range) && math.abs(speed) > MathHelpers.Epsilon)
                        speed = MathHelpers.Damp(v - v0, decelTime, deltaTime) / deltaTime;
                }
                input = speed * deltaTime;
            }

            axis.value = axis.ClampValue(axis.value + input);
            return math.abs(inputValue) > MathHelpers.Epsilon;
        }
    }
}
