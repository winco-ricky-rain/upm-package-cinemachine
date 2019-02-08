using UnityEngine;
using System;
using Unity.Entities;
using Cinemachine.ECS;
using Unity.Mathematics;
using Unity.Transforms;

namespace Cinemachine.ECS_Hybrid
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [RequireComponent(typeof(GameObjectEntity))]
    [AddComponentMenu("Cinemachine/CM_Vcam")]
    public class CM_Vcam : MonoBehaviour, ICinemachineCamera
    {
        /// <summary>The priority will determine which camera becomes active based on the
        /// state of other cameras and this camera.  Higher numbers have greater priority.
        /// </summary>
        [NoSaveDuringPlay]
        [Tooltip("The priority will determine which camera becomes active based on the "
            + "state of other cameras and this camera.  Higher numbers have greater priority.")]
        public int m_Priority = 10;

        /// <summary>Object for the camera children to look at (the aim target)</summary>
        [Tooltip("Object for the camera children to look at (the aim target).")]
        [NoSaveDuringPlay]
        public Transform m_LookAtTarget = null;

        /// <summary>Object for the camera children wants to move with (the body target)</summary>
        [Tooltip("Object for the camera children wants to move with (the body target).")]
        [NoSaveDuringPlay]
        public Transform m_FollowTarget = null;

        /// <summary>Specifies the LensSettings of this Virtual Camera.
        /// These settings will be transferred to the Unity camera when the vcam is live.</summary>
        [Tooltip("Specifies the lens properties of this Virtual Camera.  This generally mirrors "
            + "the Unity Camera's lens settings, and will be used to drive the Unity camera when "
            + "the vcam is active.")]
        [LensSettingsProperty]
        public LensSettings m_Lens = LensSettings.Default;

        /// <summary> Collection of parameters that influence how this virtual camera transitions from
        /// other virtual cameras </summary>
        public CinemachineVirtualCameraBase.TransitionParams m_Transitions; // GML fixme

        /// <summary>API for the editor, to make the dragging of position handles behave better.</summary>
        public bool UserIsDragging { get; set; }

        GameObjectEntity m_gameObjectEntityComponent;

        Entity Entity
        {
            get
            {
                return m_gameObjectEntityComponent == null
                    ? Entity.Null : m_gameObjectEntityComponent.Entity;
            }
        }

        EntityManager ActiveEntityManager
        {
            get
            {
                var w = World.Active;
                return w == null ? null : w.GetExistingManager<EntityManager>();
            }
        }

        public T GetEntityComponentData<T>() where T : struct, IComponentData
        {
            var m = ActiveEntityManager;
            if (m != null)
                if (m.HasComponent<T>(Entity))
                    return m.GetComponentData<T>(Entity);
            return new T();
        }

        /// <summary>Get the name of the Virtual Camera</summary>
        public string Name { get { return name; } }

        /// <summary>Gets a brief debug description of this virtual camera, for
        /// use when displayiong debug info</summary>
        public virtual string Description { get { return string.Empty; }}

        public bool IsValid { get { return !(this == null); } }

        /// <summary>The CameraState object holds all of the information
        /// necessary to position the Unity camera.  It is the output of this class.</summary>
        public CameraState State
        {
            get { return (CM_EntityVcam.StateFromEntity(Entity)); }
        }

        public ICinemachineCamera ParentCamera { get { return null; } }
        public bool IsLiveChild(ICinemachineCamera vcam) { return false; }
        public void UpdateCameraState(Vector3 worldUp, float deltaTime) {}

        // GML todo: this is wrong - getting rid of CinemachineCore
        public bool IsLive { get { return CinemachineCore.Instance.IsLive(this); } }

        public Entity AsEntity { get { return Entity; }}

        /// <summary>This is called to notify the vcam that a target got warped,
        /// so that the vcam can update its internal state to make the camera
        /// also warp seamlessy.</summary>
        /// <param name="target">The object that was warped</param>
        /// <param name="positionDelta">The amount the target's position changed</param>
        public void OnTargetObjectWarped(Transform target, Vector3 positionDelta)
        {
            if (target == m_FollowTarget)
            {
                var m = ActiveEntityManager;
                if (m == null)
                    return;
                var c = m.GetComponentData<CM_VcamPositionState>(Entity);
                c.raw += new float3(positionDelta);
                m.SetComponentData(Entity, c);
                transform.position += positionDelta;
            }
/* GML todo: how to push this to other systems that need it?
            UpdateComponentCache();
            for (int i = 0; i < m_Components.Length; ++i)
            {
                if (m_Components[i] != null)
                    m_Components[i].OnTargetObjectWarped(target, positionDelta);
            }
*/
        }

        /// <summary>If we are transitioning from another FreeLook, grab the axis values from it.</summary>
        /// <param name="fromCam">The camera being deactivated.  May be null.</param>
        /// <param name="worldUp">Default world Up, set by the CinemachineBrain</param>
        /// <param name="deltaTime">Delta time for time-based effects (ignore if less than or equal to 0)</param>
        public void OnTransitionFromCamera(
            ICinemachineCamera fromCam, Vector3 worldUp, float deltaTime)
        {
            var m = ActiveEntityManager;
            if (m == null)
                return;
            var c = m.GetComponentData<CM_VcamPositionState>(Entity);
            if (!gameObject.activeInHierarchy)
            {
                c.previousFrameDataIsValid = 0;
            }
            bool forceUpdate = false;
            if (m_Transitions.m_InheritPosition && fromCam != null)
            {
                c.raw = fromCam.State.RawPosition;
                c.previousFrameDataIsValid = 0;
                forceUpdate = true;
            }
            m.SetComponentData(Entity, c);

/* GML todo: how to push this to other systems that need it?
            for (int i = 0; i < m_Components.Length; ++i)
            {
                if (m_Components[i] != null
                        && m_Components[i].OnTransitionFromCamera(
                            fromCam, worldUp, deltaTime, ref m_Transitions))
                    forceUpdate = true;
            }
*/
            if (forceUpdate)
                InternalUpdateCameraState(worldUp, deltaTime);
            else
                UpdateCameraState(worldUp, deltaTime);
            if (m_Transitions.m_OnCameraLive != null)
                m_Transitions.m_OnCameraLive.Invoke(this, fromCam);
        }

        /// <summary>Internal use only.  Called at designated update time
        /// so the vcam can position itself and track its targets.  All 3 child rigs are updated,
        /// and a blend calculated, depending on the value of the Y axis.</summary>
        /// <param name="worldUp">Default world Up, set by the CinemachineBrain</param>
        /// <param name="deltaTime">Delta time for time-based effects (ignore if less than 0)</param>
        public void InternalUpdateCameraState(Vector3 worldUp, float deltaTime)
        {
#if false
            if (!PreviousStateIsValid)
                deltaTime = -1;

            // Initialize the camera state, in case the game object got moved in the editor
            m_State = PullStateFromVirtualCamera(worldUp, ref m_Lens);

            // Do our stuff
            SetReferenceLookAtTargetInState(ref m_State);
            InvokeComponentPipeline(ref m_State, worldUp, deltaTime);
            ApplyPositionBlendMethod(ref m_State, m_Transitions.m_BlendHint);

            // Push the raw position back to the game object's transform, so it
            // moves along with the camera.
            if (!UserIsDragging)
            {
                if (Follow != null)
                    transform.position = State.RawPosition;
                if (LookAt != null)
                    transform.rotation = State.RawOrientation;
            }
            // Signal that it's all done
            InvokePostPipelineStageCallback(this, CinemachineCore.Stage.Finalize, ref m_State, deltaTime);
            PreviousStateIsValid = true;
#endif
        }

#if false
        protected CameraState InvokeComponentPipeline(
            ref CameraState state, Vector3 worldUp, float deltaTime)
        {
            UpdateComponentCache();

            // Apply the component pipeline
            for (CinemachineCore.Stage stage = CinemachineCore.Stage.Body;
                stage < CinemachineCore.Stage.Finalize; ++stage)
            {
                var c = m_Components[(int)stage];
                if (c != null)
                    c.PrePipelineMutateCameraState(ref state);
            }
            for (CinemachineCore.Stage stage = CinemachineCore.Stage.Body;
                stage < CinemachineCore.Stage.Finalize; ++stage)
            {
                var c = m_Components[(int)stage];
                if (c != null)
                    c.MutateCameraState(ref state, deltaTime);
                else if (stage == CinemachineCore.Stage.Aim)
                    state.BlendHint |= CameraState.BlendHintValue.IgnoreLookAtTarget; // no aim
                InvokePostPipelineStageCallback(this, stage, ref state, deltaTime);
            }

            return state;
        }
#endif


#if true // GML todo something here
        Entity EnsureTargetCompliance(Transform target)
        {
            if (target == null)
                return Entity.Null;

            var m = ActiveEntityManager;
            if (m == null)
                return Entity.Null;

            var goe = target.GetComponent<GameObjectEntity>();
            if (goe == null)
                goe = target.gameObject.AddComponent<GameObjectEntity>();

            var e = goe.Entity;
            if (!m.HasComponent<CM_Target>(e))
                m.AddComponentData(e, new CM_Target());

            if (!m.HasComponent<Position>(e))
                m.AddComponentData(e, new Position());
            if (!m.HasComponent<Rotation>(e))
                m.AddComponentData(e, new Rotation());
            if (!m.HasComponent<CopyTransformFromGameObject>(e))
                m.AddComponentData(e, new CopyTransformFromGameObject());

            return e;
        }
#endif

        void PushValuesToEntityComponents()
        {
            var m = ActiveEntityManager;
            if (m == null || !m.Exists(Entity))
                return;

            if (!m.HasComponent<Position>(Entity))
                m.AddComponentData(Entity, new Position());
            if (!m.HasComponent<Rotation>(Entity))
                m.AddComponentData(Entity, new Rotation());
            if (!m.HasComponent<CopyTransformFromGameObject>(Entity))
                m.AddComponentData(Entity, new CopyTransformFromGameObject());

            if (!m.HasComponent<CM_VcamLens>(Entity))
                m.AddComponentData(Entity, CM_VcamLens.Default);
            if (!m.HasComponent<CM_VcamChannel>(Entity))
                m.AddComponentData(Entity, new CM_VcamChannel()); // GML todo: vcamSequence
            if (!m.HasComponent<CM_VcamPriority>(Entity))
                m.AddComponentData(Entity, new CM_VcamPriority()); // GML todo: vcamSequence
            if (!m.HasComponent<CM_VcamShotQuality>(Entity))
                m.AddComponentData(Entity, new CM_VcamShotQuality());

            // GML todo: GC allocs? - cache this stuff
            var e = EnsureTargetCompliance(m_FollowTarget);
            if (!m.HasComponent<CM_VcamFollowTarget>(Entity))
                m.AddComponentData(Entity, new CM_VcamFollowTarget{ target = e });
            m.SetComponentData(Entity, new CM_VcamFollowTarget{ target = e });

            e = EnsureTargetCompliance(m_LookAtTarget);
            if (!m.HasComponent<CM_VcamLookAtTarget>(Entity))
                m.AddComponentData(Entity, new CM_VcamLookAtTarget{ target = e });
            m.SetComponentData(Entity, new CM_VcamLookAtTarget{ target = e });

            m.SetComponentData(Entity, new CM_VcamLens
            {
                fov = m_Lens.Orthographic ? m_Lens.OrthographicSize : m_Lens.FieldOfView,
                nearClip = m_Lens.NearClipPlane,
                farClip = m_Lens.FarClipPlane,
                dutch = m_Lens.Dutch,
                lensShift = m_Lens.LensShift
            });

            m.SetComponentData(Entity, new CM_VcamChannel
            {
                channel = gameObject.layer // GML is this the right thing?
            });
            m.SetComponentData(Entity, new CM_VcamPriority
            {
                priority = m_Priority
                // GML todo: vcamSequence
            });
        }

        /// <summary>Updates the child rig cache</summary>
        void OnEnable()
        {
            m_gameObjectEntityComponent = GetComponent<GameObjectEntity>();
            PushValuesToEntityComponents();
        }

        // GML: Needed in editor only, probably, only if something is dirtied
        void Update()
        {
            PushValuesToEntityComponents();
        }
    }
}
