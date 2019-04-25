#if CINEMACHINE_TIMELINE

using UnityEngine;
using UnityEngine.Playables;
using Unity.Cinemachine3;
using Unity.Entities;
using Unity.Cinemachine3.Authoring;
using Cinemachine;

//namespace Unity.Cinemachine3.Authoring
//{
    internal sealed class CM_TimelineMixer : PlayableBehaviour
    {
        // The brain that this track controls
        private CM_Brain mBrain;
        private int mBrainOverrideId = -1;
        private bool mPlaying;

        static CM_ChannelSystem ActiveChannelSystem
        {
            get { return World.Active?.GetExistingSystem<CM_ChannelSystem>(); }
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            var channelSystem = ActiveChannelSystem;
            if (mBrain != null && channelSystem != null)
            {
                // clean up
                channelSystem.ReleaseCameraOverride(mBrain.Channel.channel, mBrainOverrideId);
            }
            mBrainOverrideId = -1;
        }

        public override void PrepareFrame(Playable playable, FrameData info)
        {
            mPlaying = info.evaluationType == FrameData.EvaluationType.Playback;
        }

        struct ClipInfo
        {
            public ICinemachineCamera vcam;
            public float weight;
            public double localTime;
            public double duration;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            base.ProcessFrame(playable, info, playerData);

            // Get the brain that this track controls.
            // Older versions of timeline sent the gameObject by mistake.
            GameObject go = playerData as GameObject;
            if (go == null)
                mBrain = (CM_Brain)playerData;
            else
                mBrain = go.GetComponent<CM_Brain>();

            var channelSystem = ActiveChannelSystem;
            if (mBrain == null || channelSystem == null)
                return;

            // Find which clips are active.  We can process a maximum of 2.
            // In the case that the weights don't add up to 1, the outgoing weight
            // will be calculated as the inverse of the incoming weight.
            int activeInputs = 0;
            ClipInfo clipA = new ClipInfo();
            ClipInfo clipB = new ClipInfo();
            for (int i = 0; i < playable.GetInputCount(); ++i)
            {
                float weight = playable.GetInputWeight(i);
                var clip = (ScriptPlayable<CM_TimelineShotPlayable>)playable.GetInput(i);
                CM_TimelineShotPlayable shot = clip.GetBehaviour();
                if (shot != null && shot.IsValid
                    && playable.GetPlayState() == PlayState.Playing
                    && weight > 0)
                {
                    clipA = clipB;
                    clipB.vcam = shot.VirtualCamera;
                    clipB.weight = weight;
                    clipB.localTime = clip.GetTime();
                    clipB.duration = clip.GetDuration();
                    if (++activeInputs == 2)
                        break;
                }
            }

            // Figure out which clip is incoming
            bool incomingIsB = clipB.weight >= 1 || clipB.localTime < clipB.duration / 2;
            if (activeInputs == 2)
            {
                if (clipB.localTime < clipA.localTime)
                    incomingIsB = true;
                else if (clipB.localTime > clipA.localTime)
                    incomingIsB = false;
                else
                    incomingIsB = clipB.duration >= clipA.duration;
            }

            // Override the Cinemachine brain with our results
            ICinemachineCamera camA = incomingIsB ? clipA.vcam : clipB.vcam;
            ICinemachineCamera camB = incomingIsB ? clipB.vcam : clipA.vcam;
            float camWeightB = incomingIsB ? clipB.weight : 1 - clipB.weight;
            mBrainOverrideId = channelSystem.SetCameraOverride(
                mBrain.Channel.channel,
                mBrainOverrideId, 2f,
                camA, camB, camWeightB);
        }
    }
//}
#endif
