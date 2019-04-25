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
    [RequireComponent(typeof(GameObjectEntity))]
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
            : UnityEvent<ICinemachineCamera, ICinemachineCamera, bool> {}

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
                if (m_OutputCamera == null && !Application.isPlaying)
                    m_OutputCamera = GetComponent<Camera>();
                return m_OutputCamera;
            }
        }
        private Camera m_OutputCamera = null; // never use directly - use accessor

        /// <summary>API for the Unity Editor.</summary>
        /// <returns>Color used to indicate that a camera is in Solo mode.</returns>
        public static Color GetSoloGUIColor() { return Color.Lerp(Color.red, Color.yellow, 0.8f); }

        public Entity SoloCamera
        {
            get
            {
                var m = ActiveChannelSystem;
                if (m != null)
                    return m.GetSoloCamera(Channel.channel);
                return Entity.Null;
            }
            set
            {
                var m = ActiveChannelSystem;
                if (m != null)
                    m.SetSoloCamera(Channel.channel, value);
            }
        }

        /// <summary>
        /// Get the current active virtual camera.
        /// </summary>
        public ICinemachineCamera ActiveVirtualCamera
        {
            get
            {
                return ActiveChannelSystem?.GetActiveVirtualCamera(Channel.channel);
            }
        }

        /// <summary>
        /// Is there a blend in progress?
        /// </summary>
        public bool IsBlending
        {
            get
            {
                var channelSystem = ActiveChannelSystem;
                if (channelSystem != null)
                    return channelSystem.IsBlending(Channel.channel);
                return false;
            }
        }

        /// <summary>
        /// Get the current blend in progress.  Returns null if none.
        /// </summary>
        public CM_BlendState ActiveBlend
        {
            get
            {
                var channelSystem = ActiveChannelSystem;
                if (channelSystem != null)
                    return channelSystem.GetActiveBlend(Channel.channel);
                return new CM_BlendState();
            }
        }

        /// <summary>
        /// The current state applied to the unity camera (may be the result of a blend)
        /// </summary>
        public CameraState CurrentCameraState
        {
            get
            {
                var channelSystem = ActiveChannelSystem;
                if (channelSystem != null && liveVcamsPreviousFrame.Count > 0)
                    return channelSystem.GetCurrentCameraState(Channel.channel);

                var state = CameraState.Default;
                var t = transform;
                state.RawPosition = t.position;
                state.RawOrientation = t.rotation;
                var cam = OutputCamera;
                if (cam != null)
                    state.Lens = LensSettings.FromCamera(cam);
                return state;
            }
        }

        // Get the first active brain on this channel
        public static CM_Brain FindBrain(int channel)
        {
            for (int i = 0; i < sAllBrains.Count; ++i)
                if (sAllBrains[i].Channel.channel == channel)
                    return sAllBrains[i];
            return null;
        }

        static List<CM_Brain> sAllBrains = new List<CM_Brain>();

        /// Hook for custom blend - called whenever a blend is created, allowing client
        /// to override the blend definition
        public delegate CinemachineBlendDefinition CreateBlendDelegate(
            GameObject context, ICinemachineCamera fromCam, ICinemachineCamera toCam,
            CinemachineBlendDefinition defaultBlend);
        public static CreateBlendDelegate OnCreateBlend;

        /// <summary> Apply a cref="CameraState"/> to the game object</summary>
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
                Color color = GUI.color;
                sb.Length = 0;
                sb.Append("CM ");
                sb.Append(gameObject.name);
                sb.Append(": ");
                if (SoloCamera != Entity.Null)
                {
                    sb.Append("SOLO ");
                    GUI.color = GetSoloGUIColor();
                }

                if (IsBlending)
                    sb.Append(ActiveBlend.Description());
                else
                {
                    ICinemachineCamera vcam = ActiveVirtualCamera;
                    if (vcam == null)
                        sb.Append("(none)");
                    else
                    {
                        sb.Append("[");
                        sb.Append(vcam.Name);
                        sb.Append("]");
                    }
                }
                string text = sb.ToString();
                Rect r = CinemachineDebug.GetScreenPos(this, text, GUI.skin.box);
                GUI.Label(r, text, GUI.skin.box);
                GUI.color = color;
                CinemachineDebug.ReturnToPool(sb);
            }
        }

        GameObjectEntity m_gameObjectEntityComponent;

        Entity Entity
        {
            get
            {
                return m_gameObjectEntityComponent == null
                    ? Entity.Null : m_gameObjectEntityComponent.Entity;
            }
        }

        static CM_ChannelSystem ActiveChannelSystem
        {
            get { return World.Active?.GetExistingSystem<CM_ChannelSystem>(); }
        }

        CM_ChannelState ChannelState
        {
            get
            {
                var m = World.Active?.EntityManager;
                if (m != null && m.HasComponent<CM_ChannelState>(Entity))
                    return m.GetComponentData<CM_ChannelState>(Entity);
                return new CM_ChannelState();
            }
        }

        public CM_Channel Channel
        {
            get
            {
                var m = World.Active?.EntityManager;
                if (m != null && m.HasComponent<CM_Channel>(Entity))
                    return m.GetComponentData<CM_Channel>(Entity);
                return CM_Channel.Default;
            }
            set
            {
                var m = World.Active?.EntityManager;
                if (m != null && m.HasComponent<CM_Channel>(Entity))
                    m.SetComponentData(Entity, value);
            }
        }

        private void OnEnable()
        {
            m_OutputCamera = GetComponent<Camera>();
            m_gameObjectEntityComponent = GetComponent<GameObjectEntity>();
            sAllBrains.Add(this);
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
            CinemachineDebug.OnGUIHandlers += OnGuiHandler;
        }

        private void OnDisable()
        {
            sAllBrains.Remove(this);
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (CinemachineDebug.OnGUIHandlers != null)
                CinemachineDebug.OnGUIHandlers();
        }
#endif

        void ResolveUndefinedBlends()
        {
            var channelSystem = ActiveChannelSystem;
            if (channelSystem != null)
            {
                channelSystem.ResolveUndefinedBlends(
                    Channel.channel, new FetchBlendDefinition { brain = this });
            }
        }

        struct FetchBlendDefinition : IGetBlendCallback
        {
            public CM_Brain brain;
            public CM_BlendDefinition Invoke(Entity fromCam, Entity toCam)
            {
                var def = brain.Channel.defaultBlend;
                if (brain.customBlends != null)
                    def = brain.customBlends.GetBlendForVirtualCameras(fromCam, toCam, def);

                // Invoke the cusom blend callback
                if (OnCreateBlend != null)
                    def = OnCreateBlend(brain.gameObject,
                        CM_EntityVcam.GetEntityVcam(fromCam),
                        CM_EntityVcam.GetEntityVcam(toCam), def);

                return new CM_BlendDefinition
                {
                    curve = def.BlendCurve.ToECS(),
                    duration = def.m_Style == CinemachineBlendDefinition.Style.Cut ? 0 : def.m_Time
                };
            }
        }

        List<Entity> liveVcamsPreviousFrame = new List<Entity>();
        List<Entity> scratchList = new List<Entity>();

        void ProcessActiveVcam()
        {
            ResolveUndefinedBlends();
            var state = CurrentCameraState;

            // Send activation events
            var channelSystem = ActiveChannelSystem;
            var entityManager = World.Active?.EntityManager;
            if (channelSystem != null && entityManager != null)
            {
                var c = Channel;
                var deltaTime = ChannelState.deltaTime;
                var worldUp = math.mul(c.settings.worldOrientation, math.up());

                scratchList.Clear();
                channelSystem.GetLiveVcams(Channel.channel, scratchList, true); // deep
                var previous = liveVcamsPreviousFrame.Count > 0
                    ? CM_EntityVcam.GetEntityVcam(liveVcamsPreviousFrame[0]) : null;
                bool isBlending = channelSystem.IsBlending(c.channel);
                for (int i = scratchList.Count - 1; i >= 0; --i)
                {
                    var e = scratchList[i];

                    // Mark it live
                    if (entityManager.HasComponent<CM_VcamPositionState>(e))
                    {
                        var s = entityManager.GetComponentData<CM_VcamPositionState>(e);
                        s.isLive = true;
                        entityManager.SetComponentData(e, s);
                    }
                    if (!liveVcamsPreviousFrame.Contains(e))
                    {
                        var vcam = CM_EntityVcam.GetEntityVcam(e);
                        if (vcam != null)
                        {
                            // Notify incoming camera of transition.
                            // GML this is not really useful... think of something better
                            vcam.OnTransitionFromCamera(previous, worldUp, deltaTime);

                            // Send transition notification to observers
                            if (events.vcamActivatedEvent != null)
                                events.vcamActivatedEvent.Invoke(vcam, previous, isBlending);
                        }
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

        private void FixedUpdate()
        {
            if (m_UpdateMethod == UpdateMethod.FixedUpdate)
                ProcessActiveVcam();
        }

        private void Update()
        {
            var camera = OutputCamera;
            if (camera != null)
            {
                var c = Channel;
                CM_Channel.Settings.Projection p = camera.orthographic
                    ? CM_Channel.Settings.Projection.Orthographic
                    : CM_Channel.Settings.Projection.Perspective;
                if (c.settings.aspect != camera.aspect || c.settings.projection != p)
                {
                    c.settings.aspect = camera.aspect;
                    c.settings.projection = p;
                    Channel = c;
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
    }
}
