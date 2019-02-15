using UnityEngine;
using Unity.Entities;
using Cinemachine.ECS;
using Cinemachine.Utility;

namespace Cinemachine.ECS_Hybrid
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("Cinemachine/CM_ClearShot")]
    [RequireComponent(typeof(CM_ChannelComponent))]
    public class CM_ClearShot : CM_VcamBase
    {
        /// <summary>When enabled, the current camera and blend will be indicated in the game window, for debugging</summary>
        [Tooltip("When enabled, the current child camera and blend will be indicated in the game window, for debugging")]
        [NoSaveDuringPlay]
        public bool m_ShowDebugText = false;

        /// <summary>Gets a brief debug description of this virtual camera, for use when displayiong debug info</summary>
        public override string Description
        {
            get
            {
                // Show the active camera and blend
                var blend = ActiveBlend;
                if (blend.outgoingCam != Entity.Null)
                    return blend.Description();

                ICinemachineCamera vcam = ActiveVirtualCamera;
                if (vcam == null)
                    return "(none)";
                var sb = CinemachineDebug.SBFromPool();
                sb.Append("["); sb.Append(vcam.Name); sb.Append("]");
                string text = sb.ToString();
                CinemachineDebug.ReturnToPool(sb);
                return text;
            }
        }

        static CM_ChannelSystem ActiveChannelSystem
        {
            get { return World.Active?.GetExistingManager<CM_ChannelSystem>(); }
        }

        CM_ChannelState ChannelState
        {
            get
            {
                var m = ActiveEntityManager;
                if (m != null && m.HasComponent<CM_ChannelState>(Entity))
                    return m.GetComponentData<CM_ChannelState>(Entity);
                return new CM_ChannelState();
            }
            set
            {
                var m = ActiveEntityManager;
                if (m != null && m.HasComponent<CM_ChannelState>(Entity))
                    m.SetComponentData(Entity, value);
            }
        }

        CM_Channel Channel
        {
            get
            {
                var m = ActiveEntityManager;
                if (m != null)
                    return m.GetComponentData<CM_Channel>(Entity);
                return new CM_Channel();
            }
        }

        /// <summary>
        /// Get the current active virtual camera.
        /// </summary>
        public ICinemachineCamera ActiveVirtualCamera
        {
            get
            {
                return ActiveChannelSystem?.GetActiveVirtualCamera(ChannelState.channel);
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
                    return channelSystem.IsBlending(ChannelState.channel);
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
                    return channelSystem.GetActiveBlend(ChannelState.channel);
                return new CM_BlendState();
            }
        }

        protected override void PushValuesToEntityComponents()
        {
            base.PushValuesToEntityComponents();

            var m = ActiveEntityManager;
            if (m == null || !m.Exists(Entity))
                return;
        }

        ///  Will only be called if Unity Editor - never in build
        private void OnGuiHandler()
        {
            if (!m_ShowDebugText)
                CinemachineDebug.ReleaseScreenPos(this);
            else
            {
                var sb = CinemachineDebug.SBFromPool();
                sb.Append(Name); sb.Append(": "); sb.Append(Description);
                string text = sb.ToString();
                Rect r = CinemachineDebug.GetScreenPos(this, text, GUI.skin.box);
                GUI.Label(r, text, GUI.skin.box);
                CinemachineDebug.ReturnToPool(sb);
            }
        }

        /// <summary>Makes sure the internal child cache is up to date</summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
            CinemachineDebug.OnGUIHandlers += OnGuiHandler;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
        }
    }
}
