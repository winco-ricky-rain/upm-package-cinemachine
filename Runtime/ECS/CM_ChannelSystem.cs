#if true
using Cinemachine.Utility;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Cinemachine.ECS
{
    public class CM_ChannelSystem
    {
        /// <summary>
        /// When enabled, the cameras will always respond in real-time to user input and damping,
        /// even if the game is running in slow motion
        /// </summary>
        public bool m_IgnoreTimeScale = false;

        /// <summary>
        /// If set, this object's Y axis will define the worldspace Up vector for all the
        /// virtual cameras.  This is useful in top-down game environments.  If not set, Up is
        /// worldspace Y.
        /// </summary>
        public quaternion m_WorldOrientationOverride = quaternion.identity;

        /// <summary>Get the default world up for the virtual cameras.</summary>
        public float3 WorldUp { get { return math.mul(m_WorldOrientationOverride, math.up()); } }

        /// <summary>Get the default world orientation for the virtual cameras.</summary>
        public quaternion WorldOrientation { get { return m_WorldOrientationOverride; } }

        /// <summary>
        /// The blend which is used if you don't explicitly define a blend between two Virtual Cameras.
        /// </summary>
        [CinemachineBlendDefinitionProperty]
        public CinemachineBlendDefinition m_DefaultBlend
            = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.EaseInOut, 2f);

        /// <summary>
        /// This is the asset which contains custom settings for specific blends.
        /// </summary>
        public ICinemachineBlendProvider m_CustomBlends = null;

        /// <summary>Called when the current live vcam changes.  If a blend is involved,
        /// then this will be called on the first frame of the blend</summary>
        public delegate void VcamActivatedDelegate(
            ICinemachineCamera newCam, ICinemachineCamera prevCam, bool isCut);

        /// <summary>Called when the current live vcam changes.  If a blend is involved,
        /// then this will be called on the first frame of the blend</summary>
        public VcamActivatedDelegate OnVcamActivated;

        /// <summary>API for the Unity Editor. Show this camera no matter what.</summary>
        public ICinemachineCamera SoloCamera
        {
            get { return mSoloCamera; }
            set
            {
                if (value != null && !CinemachineCore.Instance.IsLive(value))
                    value.OnTransitionFromCamera(null, Vector3.up, Time.deltaTime);
                mSoloCamera = value;
            }
        }
        private static ICinemachineCamera mSoloCamera;

        /// <summary>
        /// Get the current active virtual camera.
        /// </summary>
        public ICinemachineCamera ActiveVirtualCamera
        {
            get
            {
                if (SoloCamera != null)
                    return SoloCamera;
                return mCurrentLiveCameras.DeepCamB();
            }
        }

        /// <summary>
        /// Is there a blend in progress?
        /// </summary>
        public bool IsBlending { get { return ActiveBlend != null; } }

        /// <summary>
        /// Get the current blend in progress.  Returns null if none.
        /// </summary>
        public CinemachineBlend ActiveBlend
        {
            get
            {
                if (SoloCamera != null)
                    return null;
                if (mCurrentLiveCameras.CamA == null || mCurrentLiveCameras.IsComplete)
                    return null;
                return mCurrentLiveCameras;
            }
        }

        /// <summary>Current channel state, final result of all blends</summary>
        public CameraState CurrentCameraState
        {
            get { return SoloCamera != null ? SoloCamera.State : mCurrentLiveCameras.State; }
        }

        /// <summary>
        /// True if the ICinemachineCamera the current active camera,
        /// or part of a current blend, either directly or indirectly because its parents are live.
        /// </summary>
        /// <param name="vcam">The camera to test whether it is live</param>
        /// <returns>True if the camera is live (directly or indirectly)
        /// or part of a blend in progress.</returns>
        public bool IsLive(ICinemachineCamera vcam)
        {
            if (SoloCamera == vcam)
                return true;
            if (mCurrentLiveCameras.Uses(vcam))
                return true;

            // GML todo: get rid of this parenting stuff
            ICinemachineCamera parent = vcam.ParentCamera;
            while (parent != null && parent.IsLiveChild(vcam))
            {
                if (mCurrentLiveCameras.Uses(parent))
                    return true;
                vcam = parent;
                parent = vcam.ParentCamera;
            }
            return false;
        }

        /// <summary>
        /// This API is specifically for Timeline.  Do not use it.
        /// Override the current camera and current blend.  This setting will trump
        /// any in-game logic that sets virtual camera priorities and Enabled states.
        /// This is the main API for the timeline.
        /// </summary>
        /// <param name="overrideId">Id to represent a specific client.  An internal
        /// stack is maintained, with the most recent non-empty override taking precenence.
        /// This id must be > 0.  If you pass -1, a new id will be created, and returned.
        /// Use that id for subsequent calls.  Don't forget to
        /// call ReleaseCameraOverride after all overriding is finished, to
        /// free the OverideStack resources.</param>
        /// <param name="camA"> The camera to set, corresponding to weight=0</param>
        /// <param name="camB"> The camera to set, corresponding to weight=1</param>
        /// <param name="weightB">The blend weight.  0=camA, 1=camB</param>
        /// <param name="deltaTime">override for deltaTime.  Should be Time.FixedDelta for
        /// time-based calculations to be included, -1 otherwise</param>
        /// <returns>The oiverride ID.  Don't forget to call ReleaseCameraOverride
        /// after all overriding is finished, to free the OverideStack resources.</returns>
        public int SetCameraOverride(
            int overrideId,
            ICinemachineCamera camA, ICinemachineCamera camB,
            float weightB, float deltaTime)
        {
            if (overrideId < 0)
                overrideId = mNextFrameId++;

            VcamStackFrame frame = mFrameStack[GetBrainFrame(overrideId)];
            frame.deltaTimeOverride = deltaTime;
            frame.timeOfOverride = Time.realtimeSinceStartup;
            frame.blend.CamA = camA;
            frame.blend.CamB = camB;
            frame.blend.BlendCurve = BlendCurve.Linear;
            frame.blend.Duration = 1;
            frame.blend.TimeInBlend = weightB;

            return overrideId;
        }

        /// <summary>
        /// This API is specifically for Timeline.  Do not use it.
        /// Release the resources used for a camera override client.
        /// See SetCameraOverride.
        /// </summary>
        /// <param name="overrideId">The ID to released.  This is the value that
        /// was returned by SetCameraOverride</param>
        public void ReleaseCameraOverride(int overrideId)
        {
            for (int i = mFrameStack.Count - 1; i > 0; --i)
            {
                if (mFrameStack[i].id == overrideId)
                {
                    mFrameStack.RemoveAt(i);
                    return;
                }
            }
        }


        private class VcamStackFrame
        {
            public int id;
            public CinemachineBlend blend = new CinemachineBlend(null, null, BlendCurve.Default, 0, 0);
            public bool Active { get { return blend.IsValid; } }

            // Working data - updated every frame
            public CinemachineBlend workingBlend = new CinemachineBlend(null, null, BlendCurve.Default, 0, 0);
            public BlendSourceVirtualCamera workingBlendSource = new BlendSourceVirtualCamera(null);

            // Used by Timeline Preview for overriding the current value of deltaTime
            public float deltaTimeOverride;
            public float timeOfOverride;
            public bool TimeOverrideExpired
            {
                get { return Time.realtimeSinceStartup - timeOfOverride > Time.maximumDeltaTime; }
            }
        }

        // Current game state is always frame 0, overrides are subsequent frames
        private List<VcamStackFrame> mFrameStack = new List<VcamStackFrame>();
        private int mNextFrameId = 1;

        /// Get the frame index corresponding to the ID
        private int GetBrainFrame(int withId)
        {
            int count = mFrameStack.Count;
            for (int i = mFrameStack.Count - 1; i > 0; --i)
                if (mFrameStack[i].id == withId)
                    return i;
            // Not found - add it
            mFrameStack.Add(new VcamStackFrame() { id = withId });
            return mFrameStack.Count - 1;
        }

        // Current Brain State - result of all frames.  Blend camB is "current" camera always
        CinemachineBlend mCurrentLiveCameras = new CinemachineBlend(null, null, BlendCurve.Default, 0, 0);

        private void UpdateFrame0(float deltaTime)
        {
            // Update the in-game frame (frame 0)
            VcamStackFrame frame = mFrameStack[0];

            // Are we transitioning cameras?
            var activeCamera = TopCameraFromPriorityQueue();
            var outGoingCamera = frame.blend.CamB;
            if (activeCamera != outGoingCamera)
            {
                // Do we need to create a game-play blend?
                if (activeCamera != null && activeCamera.IsValid
                    && outGoingCamera != null && outGoingCamera.IsValid && deltaTime >= 0)
                {
                    // Create a blend (time will be 0 if a cut)
                    var blendDef = LookupBlend(outGoingCamera, activeCamera);
                    if (blendDef.m_Time > 0)
                    {
                        if (frame.blend.IsComplete)
                            frame.blend.CamA = outGoingCamera;  // new blend
                        else // chain to existing blend
                            frame.blend.CamA = new BlendSourceVirtualCamera(
                                new CinemachineBlend(
                                    frame.blend.CamA, frame.blend.CamB,
                                    frame.blend.BlendCurve, frame.blend.Duration, frame.blend.TimeInBlend));

                        frame.blend.BlendCurve = blendDef.BlendCurve;
                        frame.blend.Duration = blendDef.m_Time;
                        frame.blend.TimeInBlend = 0;
                    }
                }
                // Set the current active camera
                frame.blend.CamB = activeCamera;
            }

            // Advance the current blend (if any)
            if (frame.blend.CamA != null)
            {
                frame.blend.TimeInBlend += (deltaTime >= 0) ? deltaTime : frame.blend.Duration;
                if (frame.blend.IsComplete)
                {
                    // No more blend
                    frame.blend.CamA = null;
                    frame.blend.Duration = 0;
                    frame.blend.TimeInBlend = 0;
                }
            }
        }

        private void UpdateCurrentLiveCameras()
        {
            // Resolve the current working frame states in the stack
            int lastActive = 0;
            for (int i = 0; i < mFrameStack.Count; ++i)
            {
                VcamStackFrame frame = mFrameStack[i];
                if (i == 0 || frame.Active)
                {
                    frame.workingBlend.CamA = frame.blend.CamA;
                    frame.workingBlend.CamB = frame.blend.CamB;
                    frame.workingBlend.BlendCurve = frame.blend.BlendCurve;
                    frame.workingBlend.Duration = frame.blend.Duration;
                    frame.workingBlend.TimeInBlend = frame.blend.TimeInBlend;
                    if (i > 0 && !frame.blend.IsComplete)
                    {
                        if (frame.workingBlend.CamA == null)
                        {
                            if (mFrameStack[lastActive].blend.IsComplete)
                                frame.workingBlend.CamA = mFrameStack[lastActive].blend.CamB;
                            else
                            {
                                frame.workingBlendSource.Blend = mFrameStack[lastActive].workingBlend;
                                frame.workingBlend.CamA = frame.workingBlendSource;
                            }
                        }
                        else if (frame.workingBlend.CamB == null)
                        {
                            if (mFrameStack[lastActive].blend.IsComplete)
                                frame.workingBlend.CamB = mFrameStack[lastActive].blend.CamB;
                            else
                            {
                                frame.workingBlendSource.Blend = mFrameStack[lastActive].workingBlend;
                                frame.workingBlend.CamB = frame.workingBlendSource;
                            }
                        }
                    }
                    lastActive = i;
                }
            }
            var workingBlend = mFrameStack[lastActive].workingBlend;
            mCurrentLiveCameras.CamA = workingBlend.CamA;
            mCurrentLiveCameras.CamB = workingBlend.CamB;
            mCurrentLiveCameras.BlendCurve = workingBlend.BlendCurve;
            mCurrentLiveCameras.Duration = workingBlend.Duration;
            mCurrentLiveCameras.TimeInBlend = workingBlend.TimeInBlend;
        }

        ICinemachineCamera mActiveCameraPreviousFrame;
        private void ProcessActiveCamera(float deltaTime)
        {
            var activeCamera = ActiveVirtualCamera;
            if (activeCamera != null)
            {
                // Has the current camera changed this frame?
                if (activeCamera != mActiveCameraPreviousFrame)
                {
                    // Notify incoming camera of transition
                    activeCamera.OnTransitionFromCamera(mActiveCameraPreviousFrame, WorldUp, deltaTime);

                    // Send transition notification to observers
                    if (OnVcamActivated != null)
                        OnVcamActivated.Invoke(activeCamera, mActiveCameraPreviousFrame, !IsBlending);
                }
            }
            mActiveCameraPreviousFrame = activeCamera;
        }

        /// <summary>
        /// Get the highest-priority Enabled ICinemachineCamera
        /// that is visible to my camera.  Culling Mask is used to test visibility.
        /// </summary>
        private ICinemachineCamera TopCameraFromPriorityQueue(int layerMask = ~0)
        {
            var prioritySystem = World.Active.GetExistingManager<CM_VcamPrioritySystem>();
            if (prioritySystem != null)
            {
                var queue = prioritySystem.GetPriorityQueueNow();
                for (int i = 0; i < queue.Length; ++i)
                {
                    var e = queue[i];
                    if ((layerMask & (1 << e.vcamPriority.vcamLayer)) != 0)
                        return CM_EntityVcam.GetEntityVcam(e.entity);
                }
            }
            return null;
        }

        /// <summary>
        /// Create a blend curve for blending from one ICinemachineCamera to another.
        /// If there is a specific blend defined for these cameras it will be used, otherwise
        /// a default blend will be created, which could be a cut.
        /// </summary>
        private CinemachineBlendDefinition LookupBlend(
            ICinemachineCamera fromKey, ICinemachineCamera toKey)
        {
            // Get the blend curve that's most appropriate for these cameras
            CinemachineBlendDefinition blend = m_DefaultBlend;
            if (m_CustomBlends != null)
                blend = m_CustomBlends.GetBlendForVirtualCameras(fromKey, toKey, blend);
            return blend;
        }

        private float GetEffectiveDeltaTime()
        {
            if (!Application.isPlaying)
            {
                if (SoloCamera != null)
                    return Time.unscaledDeltaTime;

                for (int i = mFrameStack.Count - 1; i > 0; --i)
                {
                    var frame = mFrameStack[i];
                    if (frame.Active)
                        return frame.TimeOverrideExpired ? -1 : frame.deltaTimeOverride;
                }
                return -1; // no damping
            }
            return m_IgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
        }

/*
        private void OnEnable()
        {
            // Make sure there is a first stack frame
            if (mFrameStack.Count == 0)
                mFrameStack.Add(new VcamStackFrame());

            m_OutputCamera = GetComponent<Camera>();
            sActiveBrains.Insert(0, this);
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
            CinemachineDebug.OnGUIHandlers += OnGuiHandler;
        }

        private void OnDisable()
        {
            CinemachineDebug.OnGUIHandlers -= OnGuiHandler;
            sActiveBrains.Remove(this);
            mFrameStack.Clear();
        }

        private void LateUpdate()
        {
            float deltaTime = GetEffectiveDeltaTime(false);
            UpdateFrame0(deltaTime);
            UpdateCurrentLiveCameras();

            // Choose the active vcam and apply it to the Unity camera
            ProcessActiveCamera(deltaTime);
        }
*/
    }
}
#endif
