using System;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Cinemachine;

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
        public static bool operator == (VirtualCamera lhs, VirtualCamera rhs) { return lhs.Entity == rhs.Entity; }

        /// <summary>
        /// VirtualCamera instances are equal if they refer to the same entity.
        /// </summary>
        /// <param name="lhs">An VirtualCamera object.</param>
        /// <param name="rhs">Another VirtualCamera object.</param>
        /// <returns>True, if entities are different.</returns>
        public static bool operator != (VirtualCamera lhs, VirtualCamera rhs) { return !(lhs == rhs); }

        /// <summary>
        /// VirtualCamera instances are equal if they refer to the same entity.
        /// </summary>
        /// <param name="compare">The object to compare to this VirtualCamera.</param>
        /// <returns>True, if the compare parameter contains an VirtualCamera object
        /// wrapping the same entity
        /// as this Entity.</returns>
        public override bool Equals(object compare) { return this == (VirtualCamera)compare; }

        /// <summary>
        /// A hash used for comparisons.
        /// </summary>
        /// <returns>A unique hash code.</returns>
        public override int GetHashCode() { return Entity.GetHashCode(); }

        /// <summary>A "blank" Entity object that does not refer to an actual entity.</summary>
        public static VirtualCamera Null => new VirtualCamera();

        /// <summary>
        /// VirtualCamera instances are equal if they represent the same entity.
        /// </summary>
        /// <param name="o">The other VirtualCamera.</param>
        /// <returns>True, if the VirtualCamera instances wrap the same entity.</returns>
        public bool Equals(VirtualCamera o) { return Entity == o.Entity; }

        /// <summary>
        /// Provides a debugging string.
        /// </summary>
        /// <returns>A string containing the entity index and generational version.</returns>
        public override string ToString() { return Entity.ToString(); }

        public Entity Entity { get; set; }

        public static VirtualCamera FromEntity(Entity e)
        {
            return new VirtualCamera { Entity = e };
        }

        /// <summary>Is this a null entity?</summary>
        public bool IsNull { get { return Entity == Entity.Null; } }

        /// <summary>Does this entity really wrap a virtual camera?</summary>
        public bool IsVirtualCamera
        {
            get
            {
                var e = Entity;
                var m = World.Active?.EntityManager;
                return m != null && m.Exists(e) && m.HasComponent<CM_VcamChannel>(e);
            }
        }

        /// <summary>Is the vcam currently conrolling a Camera?</summary>
        public bool IsLive
        {
            get
            {
                // GML todo: replace this with flag in the vcam state
                var m = World.Active?.GetExistingSystem<CM_ChannelSystem>();
                return m == null ? false : m.IsLive(this);
            }
        }

        /// <summary>What channel this vcam is on</summary>
        public int ChannelValue
        {
            get
            {
                var e = Entity;
                var m = World.Active?.EntityManager;
                if (m != null && m.Exists(e) && m.HasComponent<CM_VcamChannel>(e))
                    return m.GetSharedComponentData<CM_VcamChannel>(e).channel;
                return 0;
            }
        }

        /// <summary>Walk up the chain of vcam parents to the top channel</summary>
        public int FindTopLevelChannel()
        {
            var ch = new ChannelHelper(ChannelValue);
            var parentVcam = FromEntity(ch.Entity);
            while (parentVcam.IsVirtualCamera)
            {
                ch = new ChannelHelper(parentVcam.ChannelValue);
                parentVcam = FromEntity(ch.Entity);
            }
            return ch.Channel.channel;
        }

        /// <summary>Display name for the vcam, also used in custom blend asset</summary>
        public string Name
        {
            get
            {
                var e = Entity;
                var m = World.Active?.EntityManager;
                if (m != null && m.Exists(e))
                {
                    if (m.HasComponent<Transform>(e))
                        return m.GetComponentObject<Transform>(e).name
                            + " " + Entity.ToString() // GML temp debugging
                            ;
                }
                // GML todo: entity name
                return IsNull ? "(null)" : Entity.ToString();
            }
        }

        /// <summary>Debug description of the vcam, if it's a channel</summary>
        public string Description
        {
            get
            {
                var ch = new ChannelHelper(Entity);
                if (!ch.IsChannel)
                    return string.Empty;
                var blend = ch.ActiveBlend;
                if (blend.outgoingCam != Entity.Null)
                    return blend.Description();
                return ch.ActiveVirtualCamera.Name;
            }
        }

        /// <summary>Current state of the vcam or channel, may be the result of blends</summary>
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
                    var ch = new ChannelHelper(Entity, m);
                    if (ch.IsChannel)
                        return ch.CameraState;

                    // Fetch the state from the relevant components
                    CameraState.BlendHintValue suppress = 0;
                    if (!m.HasComponent<CM_VcamLensState>(e))
                        suppress |= CameraState.BlendHintValue.NoLens;
                    else
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
                    }
                    if (!m.HasComponent<CM_VcamPositionState>(e))
                        suppress |= CameraState.BlendHintValue.NoPosition;
                    else
                    {
                        var c = m.GetComponentData<CM_VcamPositionState>(e);
                        state.RawPosition = c.raw;
                        state.ReferenceUp = c.up;
                        state.PositionCorrection = c.correction;
                    }
                    if (!m.HasComponent<CM_VcamRotationState>(e))
                        suppress |= CameraState.BlendHintValue.NoOrientation;
                    else
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
                    }
                    state.BlendHint |= suppress;
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
