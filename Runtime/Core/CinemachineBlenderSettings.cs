using UnityEngine;
using System;
using Unity.Entities;
using Cinemachine.ECS;

namespace Cinemachine
{
    /// <summary>
    /// Asset that defines the rules for blending between Virtual Cameras.
    /// </summary>
    [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
    [Serializable]
    public sealed class CinemachineBlenderSettings
        : ScriptableObject, ICinemachineBlendProvider, ICinemachineEntityBlendProvider
    {
        /// <summary>
        /// Container specifying how two specific Cinemachine Virtual Cameras
        /// blend together.
        /// </summary>
        [DocumentationSorting(DocumentationSortingAttribute.Level.UserRef)]
        [Serializable]
        public struct CustomBlend
        {
            [Tooltip("When blending from this camera")]
            public string m_From;

            [Tooltip("When blending to this camera")]
            public string m_To;

            [CinemachineBlendDefinitionProperty]
            [Tooltip("Blend curve definition")]
            public CinemachineBlendDefinition m_Blend;
        }
        /// <summary>The array containing explicitly defined blends between two Virtual Cameras</summary>
        [Tooltip("The array containing explicitly defined blends between two Virtual Cameras")]
        public CustomBlend[] m_CustomBlends = null;

        /// <summary>Internal API for the inspector editopr: a label to represent any camera</summary>
        public const string kBlendFromAnyCameraLabel = "**ANY CAMERA**";

        /// <summary>
        /// Attempts to find a blend definition which matches the to and from cameras as specified.
        /// If no match is found, the function returns the supplied default blend.
        /// </summary>
        /// <param name="fromCameraName">The game object name of the from camera</param>
        /// <param name="toCameraName">The game object name of the to camera</param>
        /// <param name="defaultBlend">Blend to return if no custom blend found.</param>
        /// <returns></returns>
        public CinemachineBlendDefinition GetBlendForVirtualCameras(
            string fromCameraName, string toCameraName, CinemachineBlendDefinition defaultBlend)
        {
            bool gotAnyToMe = false;
            bool gotMeToAny = false;
            CinemachineBlendDefinition anyToMe = defaultBlend;
            CinemachineBlendDefinition meToAny = defaultBlend;
            if (m_CustomBlends != null)
            {
                for (int i = 0; i < m_CustomBlends.Length; ++i)
                {
                    // Attempt to find direct name first
                    CustomBlend blendParams = m_CustomBlends[i];
                    if ((blendParams.m_From == fromCameraName)
                        && (blendParams.m_To == toCameraName))
                    {
                        return blendParams.m_Blend;
                    }
                    // If we come across applicable wildcards, remember them
                    if (blendParams.m_From == kBlendFromAnyCameraLabel)
                    {
                        if (!string.IsNullOrEmpty(toCameraName)
                            && blendParams.m_To == toCameraName)
                        {
                            anyToMe = blendParams.m_Blend;
                            gotAnyToMe = true;
                        }
                        else if (blendParams.m_To == kBlendFromAnyCameraLabel)
                            defaultBlend = blendParams.m_Blend;
                    }
                    else if (blendParams.m_To == kBlendFromAnyCameraLabel
                             && !string.IsNullOrEmpty(fromCameraName)
                             && blendParams.m_From == fromCameraName)
                    {
                        meToAny = blendParams.m_Blend;
                        gotMeToAny = true;
                    }
                }
            }

            // If nothing is found try to find wild card blends from any
            // camera to our new one
            if (gotAnyToMe)
                return anyToMe;

            // Still have nothing? Try from our camera to any camera
            if (gotMeToAny)
                return meToAny;

            return defaultBlend;
        }

        public CinemachineBlendDefinition GetBlendForVirtualCameras(
            ICinemachineCamera fromCam, ICinemachineCamera toCam,
            CinemachineBlendDefinition defaultBlend)
        {
            var fromName = fromCam == null ? string.Empty : fromCam.Name;
            var toName = toCam == null ? string.Empty : toCam.Name;
            return GetBlendForVirtualCameras(fromName, toName, defaultBlend);
        }

        public CinemachineBlendDefinition GetBlendForVirtualCameras(
            Entity fromCam, Entity toCam,
            CinemachineBlendDefinition defaultBlend)
        {
            var f = CM_EntityVcam.GetEntityVcam(fromCam);
            var t = CM_EntityVcam.GetEntityVcam(toCam);
            var fromName = f == null ? string.Empty : f.Name;
            var toName = t == null ? string.Empty : t.Name;
            return GetBlendForVirtualCameras(fromName, toName, defaultBlend);
        }
    }
}
