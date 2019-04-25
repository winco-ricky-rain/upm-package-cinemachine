#if CINEMACHINE_TIMELINE

using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Unity.Cinemachine3.Authoring;

//namespace Unity.Cinemachine3.Authoring
//{
    [Serializable]
    [TrackClipType(typeof(CM_TimelineShot))]
    [TrackBindingType(typeof(CM_Brain), TrackBindingFlags.None)]
    [TrackColor(0.53f, 0.0f, 0.08f)]
    public class CM_TimelineTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(
            PlayableGraph graph, GameObject go, int inputCount)
        {
            // Hack to set the display name of the clip to match the vcam
            foreach (var c in GetClips())
            {
                CM_TimelineShot shot = (CM_TimelineShot)c.asset;
                CM_VcamBase vcam = shot.VirtualCamera.Resolve(graph.GetResolver());
                if (vcam != null)
                    c.displayName = vcam.Name;
            }

            var mixer = ScriptPlayable<CM_TimelineMixer>.Create(graph);
            mixer.SetInputCount(inputCount);
            return mixer;
        }
    }
//}
#endif
