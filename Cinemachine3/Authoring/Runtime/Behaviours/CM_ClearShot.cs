using UnityEngine;
using Cinemachine;
using Cinemachine.Utility;
using Unity.Cinemachine.Common;
using Unity.Entities;
using Unity.Mathematics;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [AddComponentMenu("Cinemachine/CM_ClearShot")]
    public class CM_ClearShot : CM_VcamBase
    {
        /// <summary>When enabled, the current camera and blend will be indicated
        /// in the game window, for debugging</summary>
        [Tooltip("When enabled, the current child camera and blend will be indicated in "
         + "the game window, for debugging")]
        [NoSaveDuringPlay]
        public bool showDebugText = false;

        [HideFoldout]
        public CM_Channel Channel;

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

        public override void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            base.Convert(entity, dstManager, conversionSystem);
            dstManager.AddComponentData(entity, Channel);
        }

        protected override void OnValidate()
        {
            var c = Channel;
            c.Validate();
            Channel = c;
            base.OnValidate();
        }

        private void Reset()
        {
            Channel = new CM_Channel
            {
                channel = -1, // GML hack
                settings = new CM_Channel.Settings { worldOrientation = quaternion.identity },
                sortMode = CM_Channel.SortMode.QualityThenPriority,
                defaultBlend = new CinemachineBlendDefinition
                {
                    m_Style = CinemachineBlendDefinition.Style.Cut,
                    m_Time = 2f
                }
            };
        }

        /// <summary>Makes sure the internal child cache is up to date</summary>
        void OnEnable()
        {
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
            CinemachineDebug.OnGUIHandlers += OnGuiHandler;
        }

        void OnDisable()
        {
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
        }

        protected override void Update()
        {
            base.Update();

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
        }
    }
}
