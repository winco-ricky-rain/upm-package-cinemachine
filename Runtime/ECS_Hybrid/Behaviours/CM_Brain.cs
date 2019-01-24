using Cinemachine.ECS;
using Cinemachine.Utility;
using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Events;

namespace Cinemachine.ECS_Hybrid
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [UpdateAfter(typeof(CM_ChannelSystem))]
    [SaveDuringPlay]
    [RequireComponent(typeof(GameObjectEntity))]
    [RequireComponent(typeof(CM_ChannelComponent))]
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
        /// When enabled, shows the camera's frustum in the scene view.
        /// </summary>
        [Tooltip("When enabled, the camera's frustum will be shown at all times in the scene view")]
        public bool m_ShowCameraFrustum = true;

        /// <summary>Event with a CM_Brain parameter</summary>
        [Serializable] public class BrainEvent : UnityEvent<CM_Brain> {}

        /// <summary>This event will fire after a brain updates its Camera</summary>
        public BrainEvent CameraUpdatedEvent = new BrainEvent();

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

        static CM_ChannelSystem ActiveChannelSystem
        {
            get { return World.Active?.GetExistingManager<CM_ChannelSystem>(); }
        }

        /// <summary>
        /// API for the Unity Editor.
        /// Show this camera no matter what.  This is static, and so affects all Cinemachine brains.
        /// </summary>
        public static ICinemachineCamera SoloCamera
        {
            get
            {
                return ActiveChannelSystem?.SoloCamera;
            }
            set
            {
                var channelSystem = ActiveChannelSystem;
                if (channelSystem != null)
                    channelSystem.SoloCamera = value;
            }
        }

        /// <summary>API for the Unity Editor.</summary>
        /// <returns>Color used to indicate that a camera is in Solo mode.</returns>
        public static Color GetSoloGUIColor() { return Color.Lerp(Color.red, Color.yellow, 0.8f); }

        /// <summary>
        /// Get the current active virtual camera.
        /// </summary>
        public ICinemachineCamera ActiveVirtualCamera
        {
            get
            {
                return ActiveChannelSystem?.GetActiveVirtualCamera(mChannelComponent.Value.channel);
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
                    return channelSystem.IsBlending(mChannelComponent.Value.channel);
                return false;
            }
        }

        /// <summary>
        /// Get the current blend in progress.  Returns null if none.
        /// </summary>
        public CM_Blender.BlendState ActiveBlend
        {
            get
            {
                var cam = SoloCamera;
                if (cam != null)
                    return new CM_Blender.BlendState { cam = cam.AsEntity, weight = 1 };
                var channelSystem = ActiveChannelSystem;
                if (channelSystem != null)
                    return channelSystem.GetActiveBlend(mChannelComponent.Value.channel);
                return new CM_Blender.BlendState();
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
                if (channelSystem != null)
                    return channelSystem.GetCurrentCameraState(
                        mChannelComponent.Value.channel);
                var state = CameraState.Default;
                var t = transform;
                state.RawPosition = t.position;
                state.RawOrientation = t.rotation;
                return state;
            }
        }

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
#if UNITY_2018_2_OR_NEWER
                    else
                    {
                        cam.usePhysicalProperties = state.Lens.IsPhysicalCamera;
                        cam.lensShift = state.Lens.LensShift;
                    }
#endif
                }
            }
            if (CameraUpdatedEvent != null)
                CameraUpdatedEvent.Invoke(this);
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
                if (SoloCamera != null)
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

        CM_ChannelComponent mChannelComponent;

        private void OnEnable()
        {
            m_OutputCamera = GetComponent<Camera>();
            mChannelComponent = GetComponent<CM_ChannelComponent>();
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
        private void OnPreCull()
        {
            PushStateToUnityCamera(CurrentCameraState);
        }
    }
}
