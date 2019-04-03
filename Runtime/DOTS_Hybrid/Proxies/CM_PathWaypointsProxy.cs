using System;
using UnityEngine;
using Unity.Entities;
using Cinemachine.ECS;
using System.Collections.Generic;
using Unity.Transforms;

namespace Cinemachine.ECS_Hybrid
{
    [DisallowMultipleComponent]
    [SaveDuringPlay]
    [RequireComponent(typeof(CM_PathProxy))]
    public class CM_PathWaypointsProxy : DynamicBufferProxy<CM_PathWaypointElement>
    {
    }
}
