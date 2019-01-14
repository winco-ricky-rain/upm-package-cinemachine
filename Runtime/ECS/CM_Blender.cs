using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine.Assertions;

namespace Cinemachine.ECS
{
    public struct CM_Blend
    {
        public ICinemachineCamera cam;
        public BlendCurve blendCurve;
        public float duration;
        public float timeInBlend;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsComplete() { return timeInBlend >= duration; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float BlendWeight()
        {
            return math.select(blendCurve.Evaluate(timeInBlend / duration), 1, IsComplete());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid() { return cam != null; }
    }

    // Support blend chaining
    public struct CM_ChainedBlend
    {
        public List<CM_Blend> stack; // GML todo: should this be native array or something?
        public int numActiveFrames;

        // Call this from main thread, before Update() gets called.  Resizes the stack.
        public void EnsureCapacity(int size)
        {
            if (stack == null)
                stack = new List<CM_Blend>(size);
            while (stack.Count < size)
                stack.Add(new CM_Blend());
        }

        public void PushEmpty()
        {
#if UNITY_ASSERTIONS
            Assert.IsTrue(stack != null, "EnsureCapacity() must be called before this");
            Assert.IsTrue(stack != null && stack.Count > numActiveFrames, "EnsureCapacity() must be called before this, with sifficient size");
#endif
            for (int i = numActiveFrames; i > 0; --i)
                stack[i] = stack[i - 1];
            stack[0] = new CM_Blend();
            ++numActiveFrames;
        }

        public CameraState GetState()
        {
            CameraState state = CameraState.Default;
            int i = numActiveFrames;
            for (--i; i >= 0; --i)
            {
                if (stack[i].cam != null)
                {
                    state = stack[i].cam.State;
                    break;
                }
            }
            for (--i; i >= 0; --i)
            {
                var frame = stack[i];
                state = CameraState.Lerp(state, frame.cam.State, frame.BlendWeight());
            }
            return state;
        }

        // Does this blend involve a specific camera?
        public bool Uses(ICinemachineCamera cam)
        {
            if (cam != null)
            {
                int numFrames = numActiveFrames;
                for (int i = 0; i < numFrames; ++i)
                    if (cam == stack[i].cam)
                        return true;
            }
            return false;
        }

        public void AdvanceBlend(float deltaTime)
        {
            int numFrames = numActiveFrames;
            for (numActiveFrames = 0; numActiveFrames < numFrames; ++numActiveFrames)
            {
                var frame = stack[numActiveFrames];
                frame.timeInBlend += deltaTime;
                stack[numActiveFrames] = frame;
                if (frame.IsComplete())
                    break;
            }
            ++numActiveFrames; // include the last one

            // Clean out any garbage created by completed blend
            for (int i = numActiveFrames; i < numFrames; ++i)
                stack[i] = new CM_Blend();
        }
    }

    public struct CM_Blender
    {
        struct Frame
        {
            public int id;
            public ICinemachineCamera camA; // outgoing cam
            public ICinemachineCamera camB; // current cam
            public float weightB;
        }

        private List<Frame> mFrameStack; // GML todo: should this be native array or something?
        private int mLastFrameId;

        CM_ChainedBlend mNativeFrame;
        CM_ChainedBlend mCurrentBlend;

        /// <summary>Get the current active virtual camera.</summary>
        public ICinemachineCamera ActiveVirtualCamera
        {
            get
            {
                if (mCurrentBlend.stack == null || mCurrentBlend.stack.Count == 0)
                    return null;
                return mCurrentBlend.stack[0].cam;
            }
        }

        /// <summary>Is there a blend in progress?</summary>
        public bool IsBlending
        {
            get
            {
                if (mCurrentBlend.stack == null || mCurrentBlend.numActiveFrames < 2)
                    return false;
                return !mCurrentBlend.stack[0].IsComplete();
            }
        }

        public struct BlendState
        {
            public ICinemachineCamera cam;
            public float weight;
            public ICinemachineCamera outgoingCam;
            public CameraState cameraState; // Full result of blend (can involve more cams)
        }

        public BlendState State
        {
            get
            {
                int count = mCurrentBlend.stack == null ? 0 : mCurrentBlend.numActiveFrames;
                if (count == 0)
                    return new BlendState { cameraState = CameraState.Default };
                var blend0 = mCurrentBlend.stack[0];
                if (count == 1)
                    return new BlendState
                    {
                        cam = blend0.cam,
                        weight = 1,
                        outgoingCam = null,
                        cameraState = mCurrentBlend.GetState()
                    };
                return new BlendState
                {
                    cam = blend0.cam,
                    weight = blend0.BlendWeight(),
                    outgoingCam = mCurrentBlend.stack[1].cam,
                    cameraState = mCurrentBlend.GetState()
                };
            }
        }

        public bool IsLive(ICinemachineCamera vcam)
        {
            return mCurrentBlend.Uses(vcam);
        }

        // Call this from the main thread, before Update() gets called
        public void PreUpdate()
        {
            mNativeFrame.EnsureCapacity(mNativeFrame.numActiveFrames + 1);
            if (mFrameStack == null)
                mFrameStack = new List<Frame>();
            mCurrentBlend.EnsureCapacity(mFrameStack.Count + mNativeFrame.numActiveFrames + 2);
        }

        // Can be called from job
        public void Update(
            float deltaTime, ICinemachineCamera activeCamera,
            ICinemachineBlendProvider blendProvider,
            CinemachineBlendDefinition defaultBlend)
        {
            UpdateNativeFrame(deltaTime, activeCamera, blendProvider, defaultBlend);
            ComputeCurrentBlend();
        }

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
        /// <param name="camA"> The camera to set, corresponding to weight=0.
        /// If null, then previous camera on the blendable stack will be used</param>
        /// <param name="camB"> The camera to set, corresponding to weight=1</param>
        /// <param name="weightB">The blend weight.  0=camA, 1=camB</param>
        /// <returns>The oiverride ID.  Don't forget to call ReleaseBlendableOverride
        /// after all overriding is finished, to free the OverideStack resources.</returns>
        public int SetBlendableOverride(
            int overrideId, ICinemachineCamera camA, ICinemachineCamera camB, float weightB)
        {
            if (overrideId < 0)
                overrideId = ++mLastFrameId;

            int index = GetOrCreateBrainFrameIndex(overrideId);
            mFrameStack[index] = new Frame()
            {
                id = overrideId,
                camA = camA,
                camB = camB,
                weightB = weightB
            };

            return overrideId;
        }

        /// <summary>
        /// Release the resources used for a camera override client.
        /// See SetCameraOverride.
        /// </summary>
        /// <param name="overrideId">The ID to released.  This is the value that
        /// was returned by SetBlendableOverride</param>
        public void ReleaseBlendableOverride(int overrideId)
        {
            if (mFrameStack != null)
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
        }

        /// Get the frame index corresponding to the ID
        int GetOrCreateBrainFrameIndex(int withId)
        {
            if (mFrameStack == null)
                mFrameStack = new List<Frame>();
            int count = mFrameStack.Count;
            for (int i = 0; i < count; ++i)
                if (mFrameStack[i].id == withId)
                    return i;
            // Not found - add it
            mFrameStack.Add(new Frame() { id = withId });
            return count;
        }

        // Can be called from a job
        void UpdateNativeFrame(
            float deltaTime, ICinemachineCamera activeCamera,
            ICinemachineBlendProvider blendProvider,
            CinemachineBlendDefinition defaultBlend)
        {
            // Are we transitioning cameras?
            var blend = mNativeFrame.stack[0];
            if (activeCamera != blend.cam)
            {
                // Do we need to create a game-play blend?
                if (activeCamera != null && blend.cam != null && deltaTime >= 0)
                {
                    // Create a blend
                    var blendDef = defaultBlend;
                    if (blendProvider != null)
                        blendProvider.GetBlendForVirtualCameras(blend.cam, activeCamera, defaultBlend);
                    if (blendDef.m_Style != CinemachineBlendDefinition.Style.Cut && blendDef.m_Time > 0)
                    {
                        mNativeFrame.PushEmpty();
                        blend = new CM_Blend
                        {
                            blendCurve = blendDef.BlendCurve,
                            duration = blendDef.m_Time
                        };
                    }
                }
                // Set the current active camera
                blend.cam = activeCamera;
                mNativeFrame.stack[0] = blend;
            }

            // Advance the current blend (if any)
            mNativeFrame.AdvanceBlend(deltaTime);
        }

        // Can be called from job
        void ComputeCurrentBlend()
        {
            // Most-recent overrides dominate
            mCurrentBlend.numActiveFrames = 0;
            for (int i = mFrameStack.Count-1; i >= 0; --i)
            {
                var frame = mFrameStack[i];
                if (frame.camB != null)
                {
                    mCurrentBlend.stack[mCurrentBlend.numActiveFrames] = new CM_Blend
                    {
                        cam = frame.camB,
                        blendCurve = BlendCurve.Linear,
                        duration = 1,
                        timeInBlend = frame.weightB
                    };
                    ++mCurrentBlend.numActiveFrames;

                    if (frame.camA != null)
                    {
                        mCurrentBlend.stack[mCurrentBlend.numActiveFrames] = new CM_Blend
                        {
                            cam = frame.camA,
                            duration = 0
                        };
                        ++mCurrentBlend.numActiveFrames;
                        break; // We're done, blend is complete
                    }
                }
            }

            // If blend is incomplete, add the native frame
            if (mCurrentBlend.numActiveFrames == 0
                || !mCurrentBlend.stack[mCurrentBlend.numActiveFrames-1].IsComplete())
            {
                for (int i = 0; i < mNativeFrame.numActiveFrames; ++i)
                    mCurrentBlend.stack[mCurrentBlend.numActiveFrames++] = mNativeFrame.stack[i];
            }
        }
    }
}
