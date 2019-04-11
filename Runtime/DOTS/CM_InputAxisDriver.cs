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
        /// <summary>How fast the axis value can travel.  Increasing this number
        /// makes the behaviour more responsive to joystick input</summary>
        [Tooltip("The maximum speed of this axis in units/second")]
        public float maxSpeed;

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

        /// <summary>If set, then the raw value of the input axis will be inverted
        /// before it is used.</summary>
        [Tooltip("If set, then the raw value of the input axis will be inverted before it is used")]
        public bool invertInput;

        // Internal state
        private float mCurrentSpeed;

        /// <summary>Call from OnValidate: Make sure the fields are sensible</summary>
        public void Validate()
        {
            maxSpeed = math.max(0, maxSpeed);
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
                try
                {
                    inputValue = CinemachineCore.GetInputAxis(name);
                }
                catch (ArgumentException)
                {
                    //Debug.LogError(e.ToString());
                }
            }

            float input = inputValue;
            if (invertInput)
                input *= -1f;

            if (maxSpeed > MathHelpers.Epsilon)
            {
                float targetSpeed = input * maxSpeed;
                if (math.abs(targetSpeed) < MathHelpers.Epsilon
                    || (math.sign(mCurrentSpeed) == math.sign(targetSpeed)
                        && math.abs(targetSpeed) <  math.abs(mCurrentSpeed)))
                {
                    // Need to decelerate
                    float a = math.abs(targetSpeed - mCurrentSpeed) / math.max(MathHelpers.Epsilon, decelTime);
                    float delta = math.min(a * deltaTime, math.abs(mCurrentSpeed));
                    mCurrentSpeed -= math.sign(mCurrentSpeed) * delta;
                }
                else
                {
                    // Accelerate to the target speed
                    float a = math.abs(targetSpeed - mCurrentSpeed) / math.max(MathHelpers.Epsilon, accelTime);
                    mCurrentSpeed += math.sign(targetSpeed) * a * deltaTime;
                    if (math.sign(mCurrentSpeed) == math.sign(targetSpeed)
                        && math.abs(mCurrentSpeed) > math.abs(targetSpeed))
                    {
                        mCurrentSpeed = targetSpeed;
                    }
                }
            }

            // Clamp our max speeds so we don't go crazy
            float maxSpeedClamped = GetMaxSpeed(ref axis);
            mCurrentSpeed = math.clamp(mCurrentSpeed, -maxSpeedClamped, maxSpeedClamped);

            axis.value += mCurrentSpeed * deltaTime;
            bool isOutOfRange = (axis.value > axis.range.y) || (axis.value < axis.range.x);
            if (isOutOfRange)
            {
                if (axis.wrap)
                {
                    if (axis.value > axis.range.y)
                        axis.value = axis.range.x + (axis.value - axis.range.y);
                    else
                        axis.value = axis.range.y + (axis.value - axis.range.x);
                }
                else
                {
                    axis.value = math.clamp(axis.value, axis.range.x, axis.range.y);
                    mCurrentSpeed = 0f;
                }
            }
            return math.abs(input) > MathHelpers.Epsilon;
        }

        // MaxSpeed may be limited as we approach the range ends, in order
        // to prevent a hard bump
        private float GetMaxSpeed(ref CM_InputAxis axis)
        {
            float range = axis.range.y - axis.range.x;
            if (!axis.wrap && range > 0)
            {
                float threshold = range / 10f;
                if (mCurrentSpeed > 0 && (axis.range.y - axis.value) < threshold)
                {
                    float t = (axis.range.y - axis.value) / threshold;
                    return math.lerp(0, maxSpeed, t);
                }
                else if (mCurrentSpeed < 0 && (axis.value - axis.range.x) < threshold)
                {
                    float t = (axis.value - axis.range.x) / threshold;
                    return math.lerp(0, maxSpeed, t);
                }
            }
            return maxSpeed;
        }
    }
}
