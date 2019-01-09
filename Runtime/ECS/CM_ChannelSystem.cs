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
        public static ICinemachineCamera SoloCamera { get; set; }

        /// Manages the nested blend stack and the camera override frames
        CM_Blender mBlender;

        /// <summary>Get the current active virtual camera.</summary>
        public ICinemachineCamera ActiveVirtualCamera { get { return mBlender.ActiveVirtualCamera; } }

        /// <summary>Is there a blend in progress?</summary>
        public bool IsBlending { get { return mBlender.IsBlending; } }

        /// <summary>
        /// Get the current blend in progress.  May be degenerate, i.e. less than 2 cams
        /// </summary>
        public CM_Blender.BlendState ActiveBlend { get { return mBlender.State; } }

        /// <summary>Current channel state, final result of all blends</summary>
        public CameraState CurrentCameraState { get { return mBlender.State.cameraState; } }

        /// <summary>
        /// True if the ICinemachineCamera is the current active camera
        /// or part of a current blend, either directly or indirectly because its parents are live.
        /// </summary>
        /// <param name="vcam">The camera to test whether it is live</param>
        /// <returns>True if the camera is live (directly or indirectly)
        /// or part of a blend in progress.</returns>
        public bool IsLive(ICinemachineCamera vcam) { return mBlender.IsLive(vcam); }

        /// <summary>
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
            float weightB)
        {
            return mBlender.SetBlendableOverride(overrideId, camA, camB, weightB);
        }

        /// <summary>
        /// Release the resources used for a camera override client.
        /// See SetCameraOverride.
        /// </summary>
        /// <param name="overrideId">The ID to released.  This is the value that
        /// was returned by SetCameraOverride</param>
        public void ReleaseCameraOverride(int overrideId)
        {
            mBlender.ReleaseBlendableOverride(overrideId);
        }

        ICinemachineCamera mActiveCameraPreviousFrame;
        private void ProcessActiveCamera(float deltaTime)
        {
            var activeCamera = ActiveVirtualCamera;

            // Has the current camera changed this frame?
            if (activeCamera != mActiveCameraPreviousFrame)
            {
                // Notify incoming camera of transition
                if (activeCamera != null)
                    activeCamera.OnTransitionFromCamera(mActiveCameraPreviousFrame, WorldUp, deltaTime);

                // Send transition notification to observers
                if (OnVcamActivated != null)
                    OnVcamActivated.Invoke(activeCamera, mActiveCameraPreviousFrame, !IsBlending);
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

        private float GetEffectiveDeltaTime()
        {
            if (!Application.isPlaying)
            {
                if (SoloCamera != null)
                    return Time.unscaledDeltaTime;
                return -1; // no damping
            }
            return m_IgnoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        // GML fixme - implement this properly
        public void Update()
        {
            float deltaTime = GetEffectiveDeltaTime();
            var activeVcam = SoloCamera != null ? SoloCamera : TopCameraFromPriorityQueue();
            mBlender.PreUpdate();

            // Note: this can be jobified
            mBlender.Update(deltaTime, activeVcam, m_CustomBlends, m_DefaultBlend);

            // Choose the active vcam and apply it to the Unity camera
            ProcessActiveCamera(deltaTime);
        }

#if false
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
#endif
    }
}
