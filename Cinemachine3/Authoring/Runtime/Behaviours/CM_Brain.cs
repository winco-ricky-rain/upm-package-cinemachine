using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Events;
using Unity.Mathematics;
using Cinemachine;
using Cinemachine.Utility;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [SaveDuringPlay]
    [RequireComponent(typeof(CM_ChannelProxy))]
    [AddComponentMenu("Cinemachine/CM_Brain")]
    public class CM_Brain : MonoBehaviour
    {
        /// <summary>
        /// When enabled, the current camera and blend will be indicated in the game window, for debugging.
        /// </summary>
        [Tooltip("When enabled, the current camera and blend will be indicated in the game "
            + "window, for debugging")]
        public bool m_ShowDebugText = false;

        /// <summary>
        /// When enabled, game view guides will be drawn when vcam inspectors are active.
        /// </summary>
        [Tooltip("When enabled, game view guides will be drawn when vcam inspectors are active")]
        public bool m_ShowGameViewGuides = true;

        /// <summary>
        /// When enabled, shows the camera's frustum in the scene view.
        /// </summary>
        [Tooltip("When enabled, the camera's frustum will be shown at all times in the scene view")]
        public bool m_ShowCameraFrustum = true;

        /// <summary>This enum defines the options available for the Camera Transform update time.</summary>
        public enum UpdateMethod
        {
            /// <summary>Camera's transform is updated in FixedUpdate</summary>
            FixedUpdate,
            /// <summary>Camera's transform is updated in Update</summary>
            Update,
            /// <summary>Camera's transform is updated in LateUpdate</summary>
            LateUpdate,
            /// <summary>Camera's transform is updated in OnPreCull</summary>
            OnPreCull
        };

        /// <summary>When the Camera gets positioned by the virtual camera.</summary>
        [Tooltip("When the Camera gets positioned by the virtual camera")]
        public UpdateMethod m_UpdateMethod = UpdateMethod.OnPreCull;

        /// <summary>
        /// This is the asset which contains custom settings for specific blends.
        /// </summary>
        public CinemachineBlenderSettings customBlends;

        /// <summary>Called when the current live vcam changes.  If a blend is involved,
        /// then this will be called on the first frame of the blend</summary>
        [Serializable] public class ActivationEvent
            : UnityEvent<VirtualCamera, VirtualCamera, bool> {}

        /// <summary>Event with a CM_Brain parameter</summary>
        [Serializable] public class BrainEvent : UnityEvent<CM_Brain> {}

        [Serializable]
        public struct Events
        {
            /// <summary>Called when the current live vcam changes.  If a blend is involved,
            /// then this will be called on the first frame of the blend</summary>
            public ActivationEvent vcamActivatedEvent;

            /// <summary>This event will fire after a brain updates its Camera</summary>
            public BrainEvent cameraUpdatedEvent;
        }
        public Events events;

        /// <summary>
        /// Get the Unity Camera that is attached to this GameObject.  This is the camera
        /// that will be controlled by the brain.
        /// </summary>
        public Camera OutputCamera
        {
            get
            {
                if (outputCamera == null && !Application.isPlaying)
                    outputCamera = GetComponent<Camera>();
                return outputCamera;
            }
        }
        private Camera outputCamera = null; // never use directly - use accessor

        /// <summary>API for the Unity Editor.</summary>
        /// <returns>Color used to indicate that a camera is in Solo mode.</returns>
        public static Color GetSoloGUIColor() { return Color.Lerp(Color.red, Color.yellow, 0.8f); }

        /// <summary>Get/set the current solo vcam.</summary>
        public VirtualCamera SoloCamera
        {
            get { return new ChannelHelper(Entity).SoloCamera; }
            set { new ChannelHelper(Entity) { SoloCamera = value }; }
        }

        /// <summary>Get/set the current state that would be applied to the Camera.</summary>
        public CameraState CameraState
        {
            get { return new ChannelHelper(Entity).CameraState; }
        }

        public Entity Entity
        {
            get { return new GameObjectEntityHelper(transform, true).Entity; }
        }

        private void OnEnable()
        {
            outputCamera = GetComponent<Camera>();
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
            CinemachineDebug.OnGUIHandlers += OnGuiHandler;
        }

        private void OnDisable()
        {
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (CinemachineDebug.OnGUIHandlers != null)
                CinemachineDebug.OnGUIHandlers();
        }
#endif

        private void FixedUpdate()
        {
            if (m_UpdateMethod == UpdateMethod.FixedUpdate)
                ProcessActiveVcam();
        }

        private void Update()
        {
            // Update the channel settings to match our camera and aspect
            var camera = OutputCamera;
            if (camera != null)
            {
                var ch = new ChannelHelper(Entity);
                var c = ch.Channel;
                CM_Channel.Settings.Projection p = camera.orthographic
                    ? CM_Channel.Settings.Projection.Orthographic
                    : CM_Channel.Settings.Projection.Perspective;
                if (c.settings.aspect != camera.aspect || c.settings.projection != p)
                {
                    c.settings.aspect = camera.aspect;
                    c.settings.projection = p;
                    ch.Channel = c;
                }
            }
            if (m_UpdateMethod == UpdateMethod.Update)
                ProcessActiveVcam();
        }

        private void LateUpdate()
        {
            if (m_UpdateMethod == UpdateMethod.LateUpdate)
                ProcessActiveVcam();
        }

        private void OnPreCull()
        {
            if (m_UpdateMethod == UpdateMethod.OnPreCull)
                ProcessActiveVcam();
        }

        /// <summary> Apply a CameraState to the game object and Camera component</summary>
        private void PushStateToUnityCamera(CameraState state)
        {
            if ((state.BlendHint & CameraState.BlendHintValue.NoPosition) == 0)
                transform.position = state.FinalPosition;
            if ((state.BlendHint & CameraState.BlendHintValue.NoOrientation) == 0)
                transform.rotation = state.FinalOrientation;
            if ((state.BlendHint & CameraState.BlendHintValue.NoLens) == 0)
            {
                Camera cam = OutputCamera;
                if (cam != null)
                {
                    cam.nearClipPlane = state.Lens.NearClipPlane;
                    cam.farClipPlane = state.Lens.FarClipPlane;
                    cam.fieldOfView = state.Lens.FieldOfView;
                    if (cam.orthographic)
                        cam.orthographicSize = state.Lens.OrthographicSize;
                    else
                    {
                        cam.usePhysicalProperties = state.Lens.IsPhysicalCamera;
                        cam.lensShift = state.Lens.LensShift;
                    }
                }
            }
            if (events.cameraUpdatedEvent != null)
                events.cameraUpdatedEvent.Invoke(this);
        }

        private void OnGuiHandler()
        {
            if (!m_ShowDebugText)
                CinemachineDebug.ReleaseScreenPos(this);
            else
            {
                // Show the active camera and blend
                var sb = CinemachineDebug.SBFromPool();
                sb.Append("CM "); sb.Append(gameObject.name); sb.Append(": ");
                var ch = new ChannelHelper(Entity);
                bool solo = !ch.SoloCamera.IsNull;
                if (solo)
                    sb.Append("SOLO ");
                sb.Append(ch.IsBlending ? ch.ActiveBlend.Description() : ch.ActiveVirtualCamera.Name);

                string text = sb.ToString();
                Color color = GUI.color;
                if (solo)
                    GUI.color = GetSoloGUIColor();
                Rect r = CinemachineDebug.GetScreenPos(this, text, GUI.skin.box);
                GUI.Label(r, text, GUI.skin.box);
                GUI.color = color;
                CinemachineDebug.ReturnToPool(sb);
            }
        }

        // Wee keep track of the live cameras so we can send activation events
        List<VirtualCamera> liveVcamsPreviousFrame = new List<VirtualCamera>();
        List<VirtualCamera> scratchList = new List<VirtualCamera>();

        void ProcessActiveVcam()
        {
            var ch = new ChannelHelper(Entity);
            ch.ResolveUndefinedBlends(customBlends);
            var state = ch.CameraState;

            // Send activation events
            var channelSystem = ChannelHelper.ChannelSystem;
            if (channelSystem != null && ch.EntityManager != null)
            {
                var c = ch.Channel;
                var deltaTime = ch.ChannelState.deltaTime;
                var worldUp = math.mul(c.settings.worldOrientation, math.up());

                scratchList.Clear();
                channelSystem.GetLiveVcams(c.channel, scratchList, true); // deep
                var previous = liveVcamsPreviousFrame.Count > 0
                    ? liveVcamsPreviousFrame[0] : VirtualCamera.Null;
                bool isBlending = ch.IsBlending;
                for (int i = scratchList.Count - 1; i >= 0; --i)
                {
                    var vcam = scratchList[i];
                    if (vcam.IsNull)
                        continue; // should never happen!

                    // Mark it live - GML todo: should move this out of MonoBehaviour
                    var e = vcam.Entity;
                    if (ch.EntityManager.HasComponent<CM_VcamPositionState>(e))
                    {
                        var s = ch.EntityManager.GetComponentData<CM_VcamPositionState>(e);
                        s.isLive = true;
                        ch.EntityManager.SetComponentData(e, s);
                    }
                    if (!liveVcamsPreviousFrame.Contains(vcam))
                    {
                        // Send transition notification to observers
                        if (events.vcamActivatedEvent != null)
                            events.vcamActivatedEvent.Invoke(vcam, previous, isBlending);
                    }
                }
                var temp = liveVcamsPreviousFrame;
                liveVcamsPreviousFrame = scratchList;
                scratchList = temp;
            }
            // Move the camera
            if (liveVcamsPreviousFrame.Count > 0)
                PushStateToUnityCamera(state);
        }
    }
}
