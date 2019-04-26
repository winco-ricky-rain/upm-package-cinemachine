using UnityEngine;
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

        /// Will only be called if Unity Editor - never in build
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
            var ch = new ChannelHelper(Entity);
            var c = ch.Channel;
            var cTop = new ChannelHelper(VirtualCamera.FindTopLevelChannel()).Channel;
            var p = cTop.settings.projection;
            if (c.settings.aspect != cTop.settings.aspect || c.settings.projection != p)
            {
                c.settings.aspect = cTop.settings.aspect;
                c.settings.projection = p;
                ch.Channel = c;
            }
            ch.ResolveUndefinedBlends(customBlends);
            base.Update();
        }
    }
}
