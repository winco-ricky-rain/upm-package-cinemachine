using UnityEngine;
using Unity.Entities;
using Cinemachine;
using Cinemachine.Utility;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("Cinemachine/CM_ClearShot")]
    [RequireComponent(typeof(CM_ChannelProxy))]
    public class CM_ClearShot : CM_VcamBase
    {
        /// <summary>When enabled, the current camera and blend will be indicated
        /// in the game window, for debugging</summary>
        [Tooltip("When enabled, the current child camera and blend will be indicated in "
         + "the game window, for debugging")]
        [NoSaveDuringPlay]
        public bool showDebugText = false;

        /// <summary>
        /// This is the asset which contains custom settings for specific blends.
        /// </summary>
        public CinemachineBlenderSettings customBlends;

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

        CM_Channel Channel
        {
            get
            {
                var m = World.Active?.EntityManager;
                if (m != null)
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

        /// <summary>
        /// Get the current active virtual camera.
        /// </summary>
        public VirtualCamera ActiveVirtualCamera
        {
            get
            {
                var m = ActiveChannelSystem;
                return m == null ? VirtualCamera.Null : m.GetActiveVirtualCamera(Channel.channel);
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

        ///  Will only be called if Unity Editor - never in build
        private void OnGuiHandler()
        {
            if (!showDebugText)
                CinemachineDebug.ReleaseScreenPos(this);
            else
            {
                var sb = CinemachineDebug.SBFromPool();
                var vcam = VirtualCamera.FromEntity(Entity);
                sb.Append(vcam.Name); sb.Append(": "); sb.Append(vcam.Description);
                string text = sb.ToString();
                Rect r = CinemachineDebug.GetScreenPos(this, text, GUI.skin.box);
                GUI.Label(r, text, GUI.skin.box);
                CinemachineDebug.ReturnToPool(sb);
            }
        }

        void ResolveUndefinedBlends()
        {
            var channelSystem = ActiveChannelSystem;
            if (channelSystem != null)
            {
                channelSystem.ResolveUndefinedBlends(
                    Channel.channel, new FetchBlendDefinition { clearShot = this });
            }
        }

        struct FetchBlendDefinition : IGetBlendDefinition
        {
            public CM_ClearShot clearShot;
            public CM_BlendDefinition GetBlend(VirtualCamera fromCam, VirtualCamera toCam)
            {
                var def = clearShot.Channel.defaultBlend;
                if (clearShot.customBlends != null)
                    def = clearShot.customBlends.GetBlendForVirtualCameras(fromCam.Name, toCam.Name, def);

                // Invoke the cusom blend callback
                if (CM_Brain.OnCreateBlend != null)
                    def = CM_Brain.OnCreateBlend(clearShot.gameObject, fromCam, toCam, def);

                return new CM_BlendDefinition
                {
                    curve = def.BlendCurve,
                    duration = def.m_Style == CinemachineBlendDefinition.Style.Cut ? 0 : def.m_Time
                };
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

        protected override void Update()
        {
            var c = Channel;
            var s = ParentChannelComponent;
            var p = s.settings.projection;
            if (c.settings.aspect != s.settings.aspect || c.settings.projection != p)
            {
                c.settings.aspect = s.settings.aspect;
                c.settings.projection = p;
                Channel = c;
            }
            ResolveUndefinedBlends();
            base.Update();
        }
    }
}
