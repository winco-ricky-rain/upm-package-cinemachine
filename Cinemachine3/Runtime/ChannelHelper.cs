using Unity.Entities;
using Unity.Cinemachine.Common;
using Cinemachine;

namespace Unity.Cinemachine3
{
    /// <summary>
    /// Helper functions for doing stuff with channel entities.
    /// Instantiate as needed, don't keep.
    /// </summary>
    public struct ChannelHelper
    {
        /// <summary>Constructor to wrap a channel entity</summary>
        /// <param name="e">The entity that holds the CM_Channel component</param>
        /// <param name="m">The curent entity manager</param>
        public ChannelHelper(Entity e) { Entity = e; EntityManager = World.Active?.EntityManager; }
        public ChannelHelper(Entity e, EntityManager m) { Entity = e; EntityManager = m; }

        /// <summary>Constructor to wrap a channel entity</summary>
        /// <param name="channelValue">The value of the channel field.
        /// This constructor might cause a suync point, so use with caution</param>
        public ChannelHelper(int channelValue)
        {
            var cs = ChannelSystem;
            Entity = cs == null ? Entity.Null : cs.GetChannelEntity(channelValue);
            EntityManager = World.Active?.EntityManager;
        }

        /// <summary>The entity that holds the CM_Channel component</summary>
        public Entity Entity { get; set; }
        public EntityManager EntityManager { get; set; }

        /// <summary>Is this entity actually wrapping a channel?</summary>
        public bool IsChannel
        {
            get
            {
                return EntityManager != null && EntityManager.Exists(Entity)
                    && EntityManager.HasComponent<CM_Channel>(Entity);
            }
        }

        /// <summary>Get a current active system.  May be null</summary>
        public static T SafeGetSystem<T>() where T : ComponentSystemBase
            { return World.Active?.GetExistingSystem<T>(); }

        /// <summary>The current active channel system.  May be null</summary>
        public static CM_ChannelSystem ChannelSystem
        {
            get { return SafeGetSystem<CM_ChannelSystem>(); }
        }

        /// <summary>Does entity have a component?</summary>
        public bool HasComponent<T>()
        {
            return EntityManager != null && Entity != Entity.Null
                && EntityManager.HasComponent<T>(Entity);
        }

        /// <summary>Get component data, with all the null checks.
        /// Returns default if nonexistant</summary>
        public T SafeGetComponentData<T>() where T : struct, IComponentData
        {
            if (EntityManager != null && EntityManager.Exists(Entity))
                if (EntityManager.HasComponent<T>(Entity))
                    return EntityManager.GetComponentData<T>(Entity);
            return new T();
        }

        /// <summary>Set component data, with all the null checks. Will add if necessary</summary>
        public void SafeSetComponentData<T>(T c) where T : struct, IComponentData
        {
            if (EntityManager != null && EntityManager.Exists(Entity))
            {
                if (EntityManager.HasComponent<T>(Entity))
                    EntityManager.SetComponentData(Entity, c);
                else
                    EntityManager.AddComponentData(Entity, c);
            }
        }

        /// <summary>The channel attached to this entity</summary>
        public CM_Channel Channel
        {
            get { return SafeGetComponentData<CM_Channel>(); }
            set { SafeSetComponentData(value); }
        }

        /// <summary>The channel state attached to this entity</summary>
        public CM_ChannelState ChannelState
        {
            get { return SafeGetComponentData<CM_ChannelState>(); }
            set { SafeSetComponentData(value); }
        }

        /// <summary>Get the current active virtual camera on the channel</summary>
        public VirtualCamera ActiveVirtualCamera
        {
            get { return VirtualCamera.FromEntity(ChannelState.activeVcam); }
        }

        /// <summary>Is there a blend in progress?</summary>
        public bool IsBlending
        {
            get { return SoloCamera.IsNull
                && SafeGetComponentData<CM_ChannelBlendState>().blender.IsBlending; }
        }

        /// <summary>Get the current blend in progress.  Will be empty if none.</summary>
        public CM_BlendState ActiveBlend
        {
            get
            {
                var solo = SoloCamera;
                if (!solo.IsNull)
                    return new CM_BlendState
                    {
                        cam = solo.Entity,
                        weight = 1,
                        cameraState = solo.State
                    };
                return SafeGetComponentData<CM_ChannelBlendState>().blender.State;
            }
        }

        /// <summary>Get/set the current solo vcam.</summary>
        public VirtualCamera SoloCamera
        {
            get { return VirtualCamera.FromEntity(ChannelState.soloCamera); }
            set
            {
                var s = ChannelState;
                s.soloCamera = value.Entity;
                ChannelState = s;
            }
        }

        /// <summary>
        /// The current fully blended camera state (may or may not be the result of a blend)
        /// </summary>
        public CameraState CameraState
        {
            get { return ActiveBlend.cameraState; }
        }

        /// <summary>
        /// Call this every frame to resolve any undefined blends in the channel
        /// </summary>
        /// <param name="defaultBlend">The default blend to use if no custom overrides found</param>
        /// <param name="customBlends">The custom blend asset.  May be null</param>
        public void ResolveUndefinedBlends(CinemachineBlenderSettings customBlends)
        {
            var channelSystem = ChannelSystem;
            if (channelSystem != null)
            {
                var c = Channel;
                channelSystem.ResolveUndefinedBlends(c.channel, new GetBlendDefinition
                    {
                        entity = Entity,
                        defaultBlend = c.defaultBlend,
                        customBlends = customBlends
                     });
            }
        }

        struct GetBlendDefinition : IGetBlendDefinition
        {
            public Entity entity;
            public CinemachineBlendDefinition defaultBlend;
            public CinemachineBlenderSettings customBlends;
            public CM_BlendDefinition GetBlend(VirtualCamera fromCam, VirtualCamera toCam)
            {
                var def = defaultBlend;
                if (customBlends != null)
                    def = customBlends.GetBlendForVirtualCameras(fromCam.Name, toCam.Name, def);

                // Invoke the cusom blend callback
                if (ClientHooks.OnCreateBlend != null)
                    def = ClientHooks.OnCreateBlend(entity, fromCam, toCam, def);

                return new CM_BlendDefinition
                {
                    curve = def.BlendCurve,
                    duration = def.m_Style == CinemachineBlendDefinition.Style.Cut ? 0 : def.m_Time
                };
            }
        }
    }
}
