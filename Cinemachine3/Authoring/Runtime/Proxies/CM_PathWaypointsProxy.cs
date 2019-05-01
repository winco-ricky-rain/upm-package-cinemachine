using UnityEngine;
using Unity.Entities;
using Unity.Cinemachine.Common;

namespace Unity.Cinemachine3.Authoring
{
    [DisallowMultipleComponent]
    [SaveDuringPlay]
    [AddComponentMenu("")] // Don't display in add component menu
    public class CM_PathWaypointsProxy : CM_DynamicBufferBase<CM_PathWaypointElement>
    {
        protected override void OnValidate()
        {
            var pathSystem = World.Active?.GetExistingSystem<CM_PathSystem>();
            pathSystem?.InvalidatePathCache(Entity);
            base.OnValidate();
        }
    }
}
