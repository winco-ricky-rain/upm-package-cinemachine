using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Cinemachine;
using System;
using Cinemachine.Utility;

namespace Unity.Cinemachine3
{
    /// <summary>
    /// Simple wrapper for a vcam entity, for stronger typing in the API
    /// </summary>
    public struct VirtualCamera : IEquatable<VirtualCamera>
    {
        /// <summary>
        /// VirtualCamera instances are equal if they refer to the same entity.
        /// </summary>
        /// <param name="lhs">An VirtualCamera object.</param>
        /// <param name="rhs">Another VirtualCamera object.</param>
        /// <returns>True, if both Entities are identical.</returns>
        public static bool operator == (VirtualCamera lhs, VirtualCamera rhs)
        {
            return lhs.Entity == rhs.Entity;
        }

        /// <summary>
        /// VirtualCamera instances are equal if they refer to the same entity.
        /// </summary>
        /// <param name="lhs">An VirtualCamera object.</param>
        /// <param name="rhs">Another VirtualCamera object.</param>
        /// <returns>True, if entities are different.</returns>
        public static bool operator != (VirtualCamera lhs, VirtualCamera rhs)
        {
            return !(lhs == rhs);
        }

        /// <summary>
        /// VirtualCamera instances are equal if they refer to the same entity.
        /// </summary>
        /// <param name="compare">The object to compare to this VirtualCamera.</param>
        /// <returns>True, if the compare parameter contains an VirtualCamera object
        /// wrapping the same entity
        /// as this Entity.</returns>
        public override bool Equals(object compare)
        {
            return this == (VirtualCamera) compare;
        }

        /// <summary>
        /// A hash used for comparisons.
        /// </summary>
        /// <returns>A unique hash code.</returns>
        public override int GetHashCode()
        {
            return Entity.GetHashCode();
        }

        /// <summary>
        /// A "blank" Entity object that does not refer to an actual entity.
        /// </summary>
        public static VirtualCamera Null => new VirtualCamera();

        /// <summary>
        /// VirtualCamera instances are equal if they represent the same entity.
        /// </summary>
        /// <param name="o">The other VirtualCamera.</param>
        /// <returns>True, if the VirtualCamera instances wrap the same entity.</returns>
        public bool Equals(VirtualCamera o)
        {
            return Entity == o.Entity;
        }

        /// <summary>
        /// Provides a debugging string.
        /// </summary>
        /// <returns>A string containing the entity index and generational version.</returns>
        public override string ToString()
        {
            return Entity.ToString();
        }

        public Entity Entity { get; set; }

        public static VirtualCamera FromEntity(Entity e)
        {
            return new VirtualCamera { Entity = e };
        }

        public bool IsNull { get { return Entity == Entity.Null; } }

        public bool IsLive
        {
            get
            {
                var m = World.Active?.GetExistingSystem<CM_ChannelSystem>();
                return m == null ? false : m.IsLive(this);
            }
        }

        public string Name
        {
            get
            {
                // GML TODO
                return IsNull ? string.Empty : Entity.ToString();
            }
        }

        public string Description
        {
            get
            {
                var m = World.Active?.EntityManager;
                var e = Entity;
                if (m == null || e == Entity.Null || !m.HasComponent<CM_Channel>(e))
                    return string.Empty;
                var cs = World.Active?.GetExistingSystem<CM_ChannelSystem>();
                if (cs == null)
                    return string.Empty;

                // Show the active camera and blend
                var c = m.GetComponentData<CM_Channel>(e).channel;
                var blend = cs.GetActiveBlend(c);
                if (blend.outgoingCam != Entity.Null)
                    return blend.Description();

                var vcam = cs.GetActiveVirtualCamera(c);
                if (vcam.IsNull)
                    return "(none)";

                var sb = CinemachineDebug.SBFromPool();
                sb.Append("["); sb.Append(vcam.Name); sb.Append("]");
                string text = sb.ToString();
                CinemachineDebug.ReturnToPool(sb);
                return text;
            }
        }

        public CameraState State
        {
            get
            {
                CameraState state = CameraState.Default;
                var m = World.Active?.EntityManager;
                var e = Entity;
                if (m != null && e != Entity.Null)
                {
                    // Is this entity a channel?
                    if (m.HasComponent<CM_ChannelBlendState>(e) && m.HasComponent<CM_Channel>(e))
                    {
                        if (m.GetSharedComponentData<CM_VcamChannel>(e).channel
                                != m.GetComponentData<CM_Channel>(e).channel)
                        {
                            var blendState = m.GetComponentData<CM_ChannelBlendState>(e);
                            return blendState.blender.State.cameraState;
                        }
                    }

                    bool noLens = true;
                    if (m.HasComponent<CM_VcamLensState>(e))
                    {
                        var c = m.GetComponentData<CM_VcamLensState>(e);
                        state.Lens = new LensSettings
                        {
                            FieldOfView = c.fov,
                            OrthographicSize = c.fov,
                            NearClipPlane = c.nearClip,
                            FarClipPlane = c.farClip,
                            Dutch = c.dutch,
                            LensShift = c.lensShift,
                            Orthographic = c.orthographic,
                            SensorSize = new Vector2(c.aspect, 1f) // GML todo: physical camera
                        };
                        noLens = false;
                    }
                    if (m.HasComponent<CM_VcamPositionState>(e))
                    {
                        var c = m.GetComponentData<CM_VcamPositionState>(e);
                        state.RawPosition = c.raw;
                        state.ReferenceUp = c.up;
                        state.PositionCorrection = c.correction;
                    }
                    if (m.HasComponent<CM_VcamRotationState>(e))
                    {
                        var c = m.GetComponentData<CM_VcamRotationState>(e);
                        state.ReferenceLookAt = c.lookAtPoint;
                        state.ReferenceLookAtRadius = c.lookAtRadius;
                        state.RawOrientation = math.normalizesafe(c.raw);
                        state.OrientationCorrection = math.normalizesafe(c.correction);
                    }
                    if (m.HasComponent<CM_VcamBlendHint>(e))
                    {
                        var c = m.GetComponentData<CM_VcamBlendHint>(e);
                        state.BlendHint = (CameraState.BlendHintValue)c.blendHint; // GML fixme
                        if (noLens)
                            state.BlendHint |= CameraState.BlendHintValue.NoLens;
                    }
                    if (m.HasComponent<CM_VcamShotQuality>(e))
                    {
                        var c = m.GetComponentData<CM_VcamShotQuality>(e);
                        state.ShotQuality = c.value;
                    }
                }
                return state;
            }
        }
    }
}
