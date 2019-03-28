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
    public class CM_PathProxy : DynamicBufferProxy<CM_PathElement>
    {
    }
}
