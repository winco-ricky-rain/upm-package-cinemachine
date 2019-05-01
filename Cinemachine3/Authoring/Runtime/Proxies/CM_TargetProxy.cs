using Unity.Mathematics;
using Unity.Cinemachine.Common;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [SaveDuringPlay]
    public class CM_TargetProxy : CM_ComponentBase<CM_Target>
    {
        protected override void OnValidate()
        {
            var v = Value;
            v.radius = math.max(0, v.radius);
            Value = v;
            base.OnValidate();
        }

        public override void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            base.Convert(entity, dstManager, conversionSystem);

            // GML temp stuff for hybrid - how to remove?
            if (!dstManager.HasComponent<Transform>(entity))
                dstManager.AddComponentObject(entity, transform);
            if (!dstManager.HasComponent<CopyTransformFromGameObject>(entity))
                dstManager.AddComponentData(entity, new CopyTransformFromGameObject());
        }

        public static Entity ValidateTarget(Transform target, bool createProxies)
        {
            if (target != null)
            {
                var t = target.GetComponent<CM_TargetProxy>();
                if (t == null && createProxies)
                    t = target.gameObject.AddComponent<CM_TargetProxy>();
                if (t != null)
                    return t.Entity;
            }
            return Entity.Null;
        }
    }
}
