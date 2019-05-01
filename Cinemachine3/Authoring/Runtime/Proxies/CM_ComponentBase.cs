using System.Collections.Generic;
using Unity.Cinemachine.Common;
using Unity.Entities;
using UnityEngine;

namespace Unity.Cinemachine3.Authoring
{
    public abstract class CM_ComponentBase<T>
        : MonoBehaviour, IConvertGameObjectToEntity where T : struct, IComponentData
    {
        [HideFoldout]
        public T Value;

        public Entity Entity
        {
            get { return new ConvertEntityHelper(transform, true).Entity; }
        }

        public virtual void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, Value);
        }

        protected virtual void OnValidate()
        {
            ConvertEntityHelper.DestroyEntity(transform);
        }
    }

    public abstract class CM_SharedComponentBase<T>
        : MonoBehaviour, IConvertGameObjectToEntity where T : struct, ISharedComponentData
    {
        [HideFoldout]
        public T Value;

        public Entity Entity
        {
            get { return new ConvertEntityHelper(transform, true).Entity; }
        }

        public virtual void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddSharedComponentData(entity, Value);
        }

        protected virtual void OnValidate()
        {
            ConvertEntityHelper.DestroyEntity(transform);
        }
    }

    public abstract class CM_DynamicBufferBase<T>
        : MonoBehaviour, IConvertGameObjectToEntity where T : struct, IBufferElementData
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
        public virtual bool IsEqual(T a, T b) { return false; }

        public Entity Entity
        {
            get { return new ConvertEntityHelper(transform, true).Entity; }
        }

        public virtual void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            var buffer = dstManager.AddBuffer<T>(entity);
            buffer.Clear();
            foreach (var element in Values)
                buffer.Add(element);
        }

        protected virtual void OnValidate()
        {
            ConvertEntityHelper.DestroyEntity(transform);
        }
    }

    public abstract class CM_VcamComponentBase<T>
        : CM_ComponentBase<T> where T : struct, IComponentData
    {
        public VirtualCamera VirtualCamera { get { return VirtualCamera.FromEntity(Entity); } }
    }
}
