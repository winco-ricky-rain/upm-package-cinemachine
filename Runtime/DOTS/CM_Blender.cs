using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;

namespace Cinemachine.ECS
{
    public delegate CM_BlendDefinition GetBlendDelegate(Entity fromCam, Entity toCam);

    public struct CM_BlendDefinition
    {
        public BlendCurve curve;
        public float duration;
    }

    public struct CM_Blend
    {
        public Entity cam;
        public BlendCurve blendCurve;
        public float duration; // if -ve, then blend curve is inverted!
        public float timeInBlend;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsComplete() { return timeInBlend >= math.abs(duration); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float BlendWeight()
        {
            var w = blendCurve.Evaluate(timeInBlend / math.abs(duration));
            return math.select(math.select(w, 1 - w, duration < 0), 1, IsComplete());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValid() { return cam != Entity.Null; }

        // Special value for undefined blends
        const float kAbsurdlyLong = 1e10f;
        public static CM_Blend UndefinedBlend { get {return new CM_Blend { duration = kAbsurdlyLong }; } }
        public bool IsUndefined() { return duration >= kAbsurdlyLong; }
    }

    // Support for blend chaining
    unsafe struct CM_ChainedBlend
    {
        CM_Blend* stack;
        int capacity;
        int length;

        public int NumActiveFrames
        {
            get { return length; }
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (value < 0 || value > capacity)
                    throw new System.IndexOutOfRangeException("CM_ChainedBlend.NumActiveFrames out of range");
#endif
                length = value;
            }
        }

        public ref CM_Blend ElementAt(int i)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (i >= length)
                throw new System.IndexOutOfRangeException("Array access out of range");
#endif
            return ref stack[i];
        }

        // Call this from main thread, before Update() gets called.  Resizes the stack.
        public void EnsureCapacity(int size)
        {
            if (stack == null || capacity < size)
            {
                var old = stack;
                stack = (CM_Blend*)UnsafeUtility.Malloc(
                    sizeof(CM_Blend) * size, UnsafeUtility.AlignOf<CM_Blend>(), Allocator.Persistent);
                UnsafeUtility.MemClear(stack, sizeof(CM_Blend) * size);
                if (length > 0)
                {
                    UnsafeUtility.MemCpy(stack, old, length * sizeof(CM_Blend));
                    UnsafeUtility.Free(old, Allocator.Persistent);
                }
                capacity = size;
            }
        }

        public void Dispose()
        {
            if (stack != null)
                UnsafeUtility.Free(stack, Allocator.Persistent);
            stack = null;
            capacity = 0;
            NumActiveFrames = 0;
        }

        public void PushEmpty()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (stack == null)
                throw new System.Exception("EnsureCapacity() must be called before this");
            if (capacity <= NumActiveFrames)
                throw new System.Exception("EnsureCapacity() must be called before this, with sufficient size");
#endif
            UnsafeUtility.MemMove(stack + 1, stack, NumActiveFrames * sizeof(CM_Blend));
            stack[0] = new CM_Blend();
            ++NumActiveFrames;
        }

        public CameraState GetState()
        {
            CameraState state = CameraState.Default;
            int i = NumActiveFrames;
            for (--i; i >= 0; --i)
            {
                if (stack[i].cam != Entity.Null)
                {
                    state = CM_EntityVcam.StateFromEntity(stack[i].cam);
                    break;
                }
            }
            for (--i; i >= 0; --i)
            {
                state = CameraState.Lerp(
                    state, CM_EntityVcam.StateFromEntity(stack[i].cam), stack[i].BlendWeight());
            }
            return state;
        }

        // Does this blend involve a specific camera?
        public bool Uses(Entity cam)
        {
            if (cam != Entity.Null)
            {
                int numFrames = NumActiveFrames;
                for (int i = 0; i < numFrames; ++i)
                    if (cam == stack[i].cam)
                        return true;
            }
            return false;
        }

        public void AdvanceBlend(float deltaTime)
        {
            int numFrames = NumActiveFrames;
            for (NumActiveFrames = 0; NumActiveFrames < numFrames; ++NumActiveFrames)
            {
                stack[NumActiveFrames].timeInBlend += deltaTime;
                if (stack[NumActiveFrames].IsComplete())
                {
                    ++NumActiveFrames; // include the last one
                    break;
                }
            }

            // Clean out any garbage created by completed blend
            for (int i = NumActiveFrames; i < numFrames; ++i)
                stack[i] = new CM_Blend();
        }
    }

    public struct CM_BlendState
    {
        public Entity cam;
        public float weight;
        public Entity outgoingCam;
        public CameraState cameraState; // Full result of blend (can involve more cams)
    }

    internal unsafe struct CM_Blender
    {
        struct Frame
        {
            public int id;
            public Entity camA; // outgoing cam
            public Entity camB; // current cam
            public float weightB;
        }

        Frame* frameStack;
        int capacity;
        public int NumOverrideFrames { get; private set; }

        private int mLastFrameId;
        private int mActiveVcamIndex;

        CM_ChainedBlend mNativeFrame;
        CM_ChainedBlend mCurrentBlend;

        public void Dispose()
        {
            mNativeFrame.Dispose();
            mCurrentBlend.Dispose();

            if (frameStack != null)
                UnsafeUtility.Free(frameStack, Allocator.Persistent);
            frameStack = null;
            capacity = 0;
            NumOverrideFrames = 0;
        }

        /// <summary>Get the current active virtual camera.</summary>
        public Entity ActiveVirtualCamera
        {
            get
            {
                if (mCurrentBlend.NumActiveFrames == 0)
                    return Entity.Null;
                return mCurrentBlend.ElementAt(mActiveVcamIndex).cam;
            }
        }

        /// <summary>Is there a blend in progress?</summary>
        public bool IsBlending
        {
            get
            {
                if (mCurrentBlend.NumActiveFrames > 1)
                {
                    var blend = mCurrentBlend.ElementAt(0);
                    if (!blend.IsUndefined() && !blend.IsComplete())
                        return true;
                }
                return false;
            }
        }

        public CM_BlendState State
        {
            get
            {
                int count = mCurrentBlend.NumActiveFrames;
                if (count == 0)
                    return new CM_BlendState { cameraState = CameraState.Default };
                var blend0 = mCurrentBlend.ElementAt(0);
                if (count == 1 || blend0.IsComplete())
                    return new CM_BlendState
                    {
                        cam = blend0.cam,
                        weight = 1,
                        outgoingCam = Entity.Null,
                        cameraState = mCurrentBlend.GetState()
                    };
                var state = new CM_BlendState
                {
                    cam = blend0.cam,
                    weight = blend0.BlendWeight(),
                    outgoingCam = mCurrentBlend.ElementAt(1).cam,
                    cameraState = mCurrentBlend.GetState()
                };
                return state;
            }
        }

        public bool IsLive(Entity vcam)
        {
            return mCurrentBlend.Uses(vcam);
        }

        public void GetLiveVcams(List<Entity> vcams)
        {
            int count = mCurrentBlend.NumActiveFrames;
            for (int i = 0; i < count; ++i)
                if (!mCurrentBlend.ElementAt(i).IsUndefined())
                    vcams.Add(mCurrentBlend.ElementAt(i).cam);
        }

        // Call this from the main thread, before Update() gets called.
        // Ensures that internal arrays are sufficiently allocated to cover the next Update
        public void PreUpdate()
        {
            mNativeFrame.EnsureCapacity(mNativeFrame.NumActiveFrames + 1);
            mCurrentBlend.EnsureCapacity(NumOverrideFrames + mNativeFrame.NumActiveFrames + 2);
        }

        public void ResolveUndefinedBlends(GetBlendDelegate blendLookup)
        {
            for (int i = 0; i < mNativeFrame.NumActiveFrames-1; ++i)
            {
                var blend = mNativeFrame.ElementAt(i);
                if (blend.IsUndefined())
                {
                    var def = blendLookup(mNativeFrame.ElementAt(i+1).cam, blend.cam);
                    blend.blendCurve = def.curve;
                    blend.duration = def.duration;
                    mNativeFrame.ElementAt(i) = blend;
                    if (blend.IsComplete())
                        mNativeFrame.NumActiveFrames = i + 1;
                }
            }
        }

        public Entity GetNewlyActivatedVcam()
        {
            if (mNativeFrame.NumActiveFrames > 0 && mNativeFrame.ElementAt(0).IsUndefined())
                return mNativeFrame.ElementAt(0).cam;
            return Entity.Null;
        }

        // Can be called from job
        public void Update(float deltaTime, Entity activeCamera)
        {
            UpdateNativeFrame(deltaTime, activeCamera);
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
            int overrideId, Entity camA, Entity camB, float weightB)
        {
            if (overrideId < 0)
                overrideId = ++mLastFrameId;

            int index = GetOrCreateBrainFrameIndex(overrideId);
            frameStack[index] = new Frame()
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
            for (int src = 0; src < NumOverrideFrames; ++src)
            {
                if (frameStack[src].id == overrideId)
                {
                    int numLeft = NumOverrideFrames - src - 1;
                    if (numLeft > 0)
                        UnsafeUtility.MemMove(
                            frameStack + src, frameStack + src + 1, numLeft * sizeof(Frame));
                    --NumOverrideFrames;
                    break;
                }
            }
        }

        /// Get the frame index corresponding to the ID
        int GetOrCreateBrainFrameIndex(int withId)
        {
            for (int i = 0; i < NumOverrideFrames; ++i)
            {
                int id = frameStack[i].id;
                if (id == withId)
                    return i;
            }
            // Not found - add it
            int newIndex = NumOverrideFrames++;
            if (capacity <= newIndex)
            {
                var old = frameStack;
                frameStack = (Frame*)UnsafeUtility.Malloc(
                    sizeof(Frame) * newIndex, UnsafeUtility.AlignOf<Frame>(), Allocator.Persistent);
                UnsafeUtility.MemClear(frameStack, sizeof(Frame) * newIndex);
                if (old != null)
                {
                    UnsafeUtility.MemCpy(frameStack, old, capacity * sizeof(Frame));
                    UnsafeUtility.Free(old, Allocator.Persistent);
                }
                capacity = NumOverrideFrames;
            }
            frameStack[newIndex] = new Frame() { id = withId };
            return newIndex;
        }

        // Can be called from a job
        void UpdateNativeFrame(float deltaTime, Entity activeCamera)
        {
            if (mNativeFrame.NumActiveFrames == 0)
                mNativeFrame.PushEmpty();

            // Are we transitioning cameras?
            var blend = mNativeFrame.ElementAt(0);
            if (activeCamera != blend.cam)
            {
                // Do we need to create a game-play blend?
                if (activeCamera != Entity.Null && blend.cam != Entity.Null && deltaTime >= 0)
                {
                    // Create an undefined blend - must be defined later or it will sit here forever
                    mNativeFrame.PushEmpty();
                    blend = CM_Blend.UndefinedBlend;
                }
                // Set the current active camera
                blend.cam = activeCamera;
                mNativeFrame.ElementAt(0) = blend;
            }

            // Advance the current blend (if any)
            mNativeFrame.AdvanceBlend(deltaTime);
        }

        // Can be called from job
        void ComputeCurrentBlend()
        {
            // Most-recent overrides dominate
            mCurrentBlend.NumActiveFrames = 0;
            mActiveVcamIndex = 0;
            for (int i = NumOverrideFrames-1; i >= 0; --i)
            {
                var frame = frameStack[i];
                if (frame.camB == Entity.Null)
                {
                    if (frame.camA == Entity.Null)
                        continue;

                    // Special case: track is only blending out
                    if (mActiveVcamIndex == mCurrentBlend.NumActiveFrames)
                        ++mActiveVcamIndex;
                    ++mCurrentBlend.NumActiveFrames;
                    mCurrentBlend.ElementAt(mCurrentBlend.NumActiveFrames-1) = new CM_Blend
                    {
                        cam = frame.camA,
                        blendCurve = BlendCurve.Linear,
                        duration = -1,
                        timeInBlend = frame.weightB
                    };
                }
                else
                {
                    ++mCurrentBlend.NumActiveFrames;
                    mCurrentBlend.ElementAt(mCurrentBlend.NumActiveFrames-1) = new CM_Blend
                    {
                        cam = frame.camB,
                        blendCurve = BlendCurve.Linear,
                        duration = 1,
                        timeInBlend = frame.weightB
                    };

                    if (frame.camA != Entity.Null)
                    {
                        ++mCurrentBlend.NumActiveFrames;
                        mCurrentBlend.ElementAt(mCurrentBlend.NumActiveFrames-1) = new CM_Blend
                        {
                            cam = frame.camA,
                            duration = 0
                        };
                        break; // We're done, blend is complete
                    }
                }
            }
            // If blend is incomplete, add the native frame
            if (mCurrentBlend.NumActiveFrames == 0
                || !mCurrentBlend.ElementAt(mCurrentBlend.NumActiveFrames-1).IsComplete())
            {
                int numFrames = mNativeFrame.NumActiveFrames;
                for (int i = 0; i < numFrames; ++i)
                    mCurrentBlend.ElementAt(++mCurrentBlend.NumActiveFrames-1) = mNativeFrame.ElementAt(i);
            }
            mActiveVcamIndex = math.min(mActiveVcamIndex, mNativeFrame.NumActiveFrames-1);
        }
    }
}
