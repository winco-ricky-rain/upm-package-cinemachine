using System.Collections.Generic;
using Unity.Cinemachine.Common;
using Unity.Entities;
using UnityEngine;

namespace Unity.Cinemachine3.Authoring
{
    [RequiresEntityConversion]
    public abstract class CM_EntityProxyBase : MonoBehaviour, IConvertGameObjectToEntity
    {
        Entity _entity;
        public Entity Entity
        {
            get
            {
                if (_entity == Entity.Null)
                    ConvertNow();
                return _entity;
            }
        }

        void ConvertNow()
        {
            DefaultWorldInitialization.DefaultLazyEditModeInitialize();
            var w = World.Active;
            if (w != null)
                GameObjectConversionUtility.ConvertGameObjectHierarchy(gameObject, w);
//            else
//                Debug.LogWarning("CM_EntityProxyBase.ConvertNow failed because there was no Active World", this);
        }

        public void DestroyEntity()
        {
            var w = World.Active;
            if (w != null && _entity != Entity.Null)
                w.EntityManager.DestroyEntity(_entity);
            _entity = Entity.Null;
        }

        protected virtual void OnEnable()
        {
            _entity = Entity.Null;
        }

        protected virtual void OnValidate()
        {
            DestroyEntity();
        }

        public virtual void Convert(
            Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            _entity = entity;
        }

        /// <summary>Add component data, but only if absent</summary>
        public bool HasComponent<T>()
        {
            var m = World.Active?.EntityManager;
            return m != null && Entity != Entity.Null && m.HasComponent<T>(Entity);
        }

        /// <summary>Add component data, but only if absent</summary>
        public void SafeAddComponentData<T>(T c) where T : struct, IComponentData
        {
            var m = World.Active?.EntityManager;
            if (m != null && Entity != Entity.Null && !m.HasComponent<T>(Entity))
                m.AddComponentData(Entity, c);
        }

        /// <summary>Get component data, with all the null checks.
        /// Returns default if nonexistant</summary>
        public T SafeGetComponentData<T>() where T : struct, IComponentData
        {
            var m = World.Active?.EntityManager;
            if (m != null && Entity != Entity.Null && m.HasComponent<T>(Entity))
                return m.GetComponentData<T>(Entity);
            return new T();
        }

        /// <summary>Set component data, with all the null checks. Will add if necessary</summary>
        public void SafeSetComponentData<T>(T c) where T : struct, IComponentData
        {
            var m = World.Active?.EntityManager;
            if (m != null && Entity != Entity.Null)
            {
                if (m.HasComponent<T>(Entity))
                    m.SetComponentData(Entity, c);
                else
                    m.AddComponentData(Entity, c);
            }
        }

        /// <summary>Set component data, with all the null checks. Will add if necessary</summary>
        public void SafeSetSharedComponentData<T>(T c) where T : struct, ISharedComponentData
        {
            var m = World.Active?.EntityManager;
            if (m != null && Entity != Entity.Null)
            {
                if (m.HasComponent<T>(Entity))
                    m.SetSharedComponentData(Entity, c);
                else
                    m.AddSharedComponentData(Entity, c);
            }
        }
    }

    public abstract class CM_ComponentBase<T>
        : CM_EntityProxyBase where T : struct, IComponentData
    {
        [HideFoldout]
        public T Value;

        public override void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            base.Convert(entity, dstManager, conversionSystem);
            dstManager.AddComponentData(entity, Value);
        }
    }

    public abstract class CM_SharedComponentBase<T>
        : CM_EntityProxyBase where T : struct, ISharedComponentData
    {
        [HideFoldout]
        public T Value;

        public override void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            base.Convert(entity, dstManager, conversionSystem);
            dstManager.AddSharedComponentData(entity, Value);
        }
    }

    public abstract class CM_DynamicBufferBase<T>
        : CM_EntityProxyBase where T : struct, IBufferElementData
    {
        [SerializeField]
        List<T> Values = new List<T>();

        public IEnumerable<T> Value { get { return Values; } }

        public void SetValue(IReadOnlyList<T> value)
        {
            Values.Clear();
            if (value != null)
            {
                if (Values.Capacity < value.Count)
                    Values.Capacity = value.Count;
                for (int i = 0, count = value.Count; i < count; ++i)
                    Values.Add(value[i]);
            }
        }

        public bool IsSame(IReadOnlyList<T> value)
        {
            bool changed = ((value == null && Values.Count != 0) || value.Count != Values.Count);
            for (int i = 0; !changed && i < Values.Count; ++i)
                changed = !IsEqual(Values[i], Values[i]);
            return changed;
        }

        /// <summary>
        /// Oveeride this to improve performance and make IsSame() valid
        /// </summary>
        protected virtual bool IsEqual(T a, T b) { return false; }

        public override void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            base.Convert(entity, dstManager, conversionSystem);
            var buffer = dstManager.AddBuffer<T>(entity);
            buffer.Clear();
            foreach (var element in Values)
                buffer.Add(element);
        }
    }

    public abstract class CM_VcamComponentBase<T>
        : CM_ComponentBase<T> where T : struct, IComponentData
    {
        public VirtualCamera VirtualCamera { get { return VirtualCamera.FromEntity(Entity); } }
    }
}
