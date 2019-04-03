using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Cinemachine.ECS
{
    [Serializable]
    public struct CM_Path : IComponentData
    {
        /// <summary>If true, then the path ends are joined to form a continuous loop</summary>
        [Tooltip("If true, then the path ends are joined to form a continuous loop.")]
        public bool looped;

        /// <summary>Path samples per waypoint</summary>
        [Tooltip("Path samples per waypoint.  This is used for calculating path distances.")]
        [Range(1, 100)]
        public int resolution;
    }

    [Serializable]
    [InternalBufferCapacity(8)]
    public struct CM_PathWaypointElement : IBufferElementData
    {
        /// <summary>Position of the waypoint in path-local space</summary>
        [Tooltip("Position of the waypoint in path-local space, and roll")]
        public float4 positionRoll;

        /// <summary>
        /// Offset from the position, which defines the tangent of the curve at the waypoint.
        /// The length of the tangent encodes the strength of the bezier handle.
        /// </summary>
        [Tooltip("Offset from the position, which defines the tangent of the curve at the waypoint.  "
            + "The length of the tangent encodes the strength of the bezier handle.")]
        public float4 tangentIn;

        /// <summary>
        /// Offset from the position, which defines the tangent of the curve at the waypoint.
        /// The length of the tangent encodes the strength of the bezier handle.
        /// </summary>
        [Tooltip("Offset from the position, which defines the tangent of the curve at the waypoint.  "
            + "The length of the tangent encodes the strength of the bezier handle.")]
        public float4 tangentOut;
    }

    /// Cache of path distances
    unsafe struct CM_PathState : ISystemStateComponentData
    {
        float2* p2d2p;   // x = p2d, y = d2p
        public int Length { get; private set; }

        public float2 p2d2pStep;
        public bool valid;

        public void Allocate(int size)
        {
            Dispose();
            if (size > 0)
            {
                p2d2p = (float2*)UnsafeUtility.Malloc(
                    sizeof(float2) * size, UnsafeUtility.AlignOf<float2>(), Allocator.Persistent);
            }
            Length = size;
        }

        public void Dispose()
        {
            if (p2d2p != null)
                UnsafeUtility.Free(p2d2p, Allocator.Persistent);
            p2d2p = null;
            Length = 0;
        }

        public ref float2 p2d2pAt(int i)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (i < 0 || i >= Length)
                throw new IndexOutOfRangeException("CM_PathState Array access out of range");
#endif
            return ref p2d2p[i];
        }

        public float PathLength { get { return Length < 2 ? 0 : p2d2p[Length-1].x; } }
    }

    [ExecuteAlways]
    [UpdateInGroup(typeof(LateSimulationSystemGroup))]
    public class CM_PathSystem : JobComponentSystem
    {
        ComponentGroup m_pathGroup;
        ComponentGroup m_missingStateGroup;
        ComponentGroup m_danglingStateGroup;

        protected override void OnCreateManager()
        {
            m_pathGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Path>(),
                ComponentType.ReadWrite<CM_PathState>(),
                ComponentType.ReadOnly(typeof(CM_PathWaypointElement)));

            m_missingStateGroup = GetComponentGroup(
                ComponentType.ReadOnly<CM_Path>(),
                ComponentType.Exclude<CM_PathState>());

            m_danglingStateGroup = GetComponentGroup(
                ComponentType.Exclude<CM_Path>(),
                ComponentType.ReadWrite<CM_PathState>());
        }

        protected override void OnDestroyManager()
        {
            // Deallocate our resources
            if (m_danglingStateGroup.CalculateLength() > 0)
            {
                var a = m_danglingStateGroup.ToEntityArray(Allocator.TempJob);
                for (int i = 0; i < a.Length; ++i)
                {
                    var s = EntityManager.GetComponentData<CM_PathState>(a[i]);
                    s.Dispose();
                }
                a.Dispose();
                EntityManager.DestroyEntity(m_danglingStateGroup);
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Add any missing group components
            if (m_missingStateGroup.CalculateLength() > 0)
                EntityManager.AddComponent(m_missingStateGroup,
                    ComponentType.ReadWrite<CM_PathState>());

            var pathJob = new DistanceCacheJob()
            {
                pathBuffers = GetBufferFromEntity<CM_PathWaypointElement>()
            };
            var pathDeps = pathJob.ScheduleGroup(m_pathGroup, inputDeps);
            return pathDeps;
        }

        //[BurstCompile] // can't because of allocations
        struct DistanceCacheJob : IJobForEachWithEntity<CM_Path, CM_PathState>
        {
            [ReadOnly] public BufferFromEntity<CM_PathWaypointElement> pathBuffers;

            public void Execute(
                Entity entity, int index,
                [ReadOnly] ref CM_Path path,
                ref CM_PathState state)
            {
                var buffer = pathBuffers[entity];
                int resolution = math.max(1, path.resolution);
                int numPoints = buffer.Length;
                float maxPos = math.select(numPoints - 1, numPoints, path.looped);
                maxPos = math.select(maxPos, 0, numPoints < 2);

                // GML temp
                ComputeSmoothTangents(ref buffer, path.looped);

                int numKeys = (int)math.round(resolution * maxPos);
                numKeys = math.select(numKeys, 0, numPoints < 2) + 1;
                if (state.valid && state.Length == numKeys)
                    return;


                // Sample the positions
                float stepSize = 1f / resolution;
                state.Allocate(numKeys);
                state.p2d2pStep = new float2(stepSize, 0);

                float pathLength = 0;
                float3 p0 = EvaluatePosition(0, path, buffer);
                state.p2d2pAt(0).x = 0;
                float pos = 0;
                for (int i = 1; i < numKeys; ++i)
                {
                    pos += stepSize;
                    float3 p = EvaluatePosition(pos, path, buffer);
                    float d = math.distance(p0, p);
                    pathLength += d;
                    p0 = p;
                    state.p2d2pAt(i).x = pathLength;
                }

                // Resample the distances
                state.p2d2pAt(0).y = 0;
                if (numKeys > 1)
                {
                    stepSize = pathLength / (numKeys - 1);
                    state.p2d2pStep.y = stepSize;
                    float distance = 0;
                    int posIndex = 1;
                    for (int i = 1; i < numKeys; ++i)
                    {
                        distance += stepSize;
                        float d = state.p2d2pAt(posIndex).x;
                        while (d < distance && posIndex < numKeys-1)
                             d = state.p2d2pAt(++posIndex).x;
                        float d0 = state.p2d2pAt(posIndex-1).x;
                        float delta = d - d0;
                        float t = (distance - d0) / delta;
                        state.p2d2pAt(i).y = state.p2d2pStep.y * (t + posIndex - 1);
                    }
                }
                state.valid = true;
            }

            void ComputeSmoothTangents(ref DynamicBuffer<CM_PathWaypointElement> waypoints, bool looped)
            {
                int numPoints = waypoints.Length;
                if (numPoints > 1)
                {
                    NativeArray<float4> K =  new NativeArray<float4>(numPoints, Allocator.Temp);
                    NativeArray<float4> p1 = new NativeArray<float4>(numPoints, Allocator.Temp);
                    NativeArray<float4> p2 = new NativeArray<float4>(numPoints, Allocator.Temp);
                    for (int i = 0; i < numPoints; ++i)
                        K[i] = p1[i] = p2[i] = waypoints[i].positionRoll;
                    if (looped)
                        BezierHelpers.ComputeSmoothControlPointsLooped(K, p1, p2);
                    else
                    {
                        BezierHelpers.ComputeSmoothControlPoints(K, p1, p2);
                        p2[numPoints-1] = K[0];
                    }

                    for (int i = 0; i < numPoints; ++i)
                    {
                        var v = waypoints[i];
                        v.tangentIn = p2[math.select(i, numPoints, i == 0) - 1] - K[i];
                        v.tangentOut = p1[i] - K[i];
                        waypoints[i] = v;
                    }
                    K.Dispose();
                    p1.Dispose();
                    p2.Dispose();
                }
            }
        }

        /// <summary>Get a standardized clamped path position, taking spins into account if looped</summary>
        /// <param name="v">Value to clamp, any units</param>
        /// <param name="maxValue">Maximum allowed path value. If looped then maxValue an 0 are the same point</param>
        /// <param name="looped">True if path is looped</param>
        /// <returns>Standardized position, between 0 and MaxPos</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ClampValue(float v, float maxValue, bool looped)
        {
            v = math.select(v, v % maxValue, looped && maxValue > MathHelpers.Epsilon);
            v = math.select(v, v + maxValue, looped && v < 0);
            return math.clamp(v, 0, maxValue);
        }

        /// <summary>Get a worldspace position of a point along the path</summary>
        /// <param name="pos">Waypoint continuous index, spins and negatives allowed if looped</param>
        /// <returns>Local-space position of the point along at path at pos</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 EvaluatePosition(
            float pos, [ReadOnly] CM_Path path, [ReadOnly] DynamicBuffer<CM_PathWaypointElement> buffer)
        {
            // GML todo: get rid of this check
            if (buffer.Length == 0)
                return float3.zero;
            float t = GetBoundingIndices(pos, buffer.Length, path.looped, out int indexA, out int indexB);
            var a = buffer[indexA].positionRoll.xyz;
            var b = buffer[indexB].positionRoll.xyz;
            return MathHelpers.Bezier(t,
                a, a + buffer[indexA].tangentOut.xyz, b + buffer[indexB].tangentIn.xyz, b);
        }

        /// <summary>Get the tangent of the curve at a point along the path.</summary>
        /// <param name="pos">Waypoint continuous index, spins and negatives allowed if looped</param>
        /// <returns>Local-space direction of the path tangent.
        /// Length of the vector represents the tangent strength</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 EvaluateTangent(
            float pos, [ReadOnly] CM_Path path, [ReadOnly] DynamicBuffer<CM_PathWaypointElement> buffer)
        {
            // GML todo: get rid of this check
            if (buffer.Length < 2)
                return float3.zero;
            float t = GetBoundingIndices(pos, buffer.Length, path.looped, out int indexA, out int indexB);
            var a = buffer[indexA].positionRoll.xyz;
            var b = buffer[indexB].positionRoll.xyz;
            return MathHelpers.BezierTangent(
                t, a, a + buffer[indexA].tangentOut.xyz, b + buffer[indexB].tangentIn.xyz, b);
        }

        /// <summary>Get the orientation the curve at a point along the path.</summary>
        /// <param name="pos">Waypoint continuous index, spins and negatives allowed if looped</param>
        /// <returns>World-space orientation of the path, as defined by tangent, up, and roll.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static quaternion EvaluateOrientation(
            float pos, [ReadOnly] CM_Path path, [ReadOnly] DynamicBuffer<CM_PathWaypointElement> buffer)
        {
            float3 fwd = EvaluateTangent(pos, path, buffer);
            if (fwd.AlmostZero())
                return quaternion.identity;

            float t = GetBoundingIndices(pos, buffer.Length, path.looped, out int indexA, out int indexB);
            float rollA = buffer[indexA].positionRoll.w;
            float rollB = buffer[indexB].positionRoll.w;
            float roll = MathHelpers.Bezier(t,
                rollA, rollA + buffer[indexA].tangentOut.w,
                rollB + buffer[indexB].tangentIn.w, rollB);

            quaternion q = quaternion.LookRotation(fwd, new float3(0, 1, 0));
            return math.mul(q, quaternion.AxisAngle(new float3(0, 0, 1), roll));
        }

        /// <summary>Returns lerp amount between bounds A and B</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float GetBoundingIndices(
            float pos, int length, bool looped, out int indexA, out int indexB)
        {
            pos = ClampValue(pos, length, looped);
            indexA = (int)math.floor(pos);
            indexA = math.select(indexA, indexA - 1, !looped && indexA > 0 && indexA == length - 1);
            indexB = math.select(indexA + 1, 0, indexA == length - 1);
//Debug.Log("pos=" + pos + " [" + indexA + "," + indexB + "]");
            return pos - indexA;
        }

        T GetEntityComponentData<T>(Entity e) where T : struct, IComponentData
        {
            if (e != Entity.Null && EntityManager != null)
            {
                if (EntityManager.HasComponent<T>(e))
                    return EntityManager.GetComponentData<T>(e);
            }
            return new T();
        }

        /// <summary>How to interpret the Path Position</summary>
        public enum PositionUnits
        {
            /// <summary>Use PathPosition units, where 0 is first waypoint, 1 is second waypoint, etc</summary>
            PathUnits,
            /// <summary>Use Distance Along Path.  Path will be sampled according to its Resolution
            /// setting, and a distance lookup table will be cached internally</summary>
            Distance,
            /// <summary>Normalized units, where 0 is the start of the path, and 1 is the end.
            /// Path will be sampled according to its Resolution
            /// setting, and a distance lookup table will be cached internally</summary>
            Normalized
        }

        /// <summary>Get the maximum value, for the given unit type</summary>
        /// <param name="path">The entity with the path</param>
        /// <param name="units">The unit type</param>
        /// <returns>The maximum allowable value for this path</returns>
        public float MaxUnit(Entity path, PositionUnits units)
        {
            if (units == PositionUnits.Normalized)
                return 1;
            if (units == PositionUnits.Distance)
            {
                var state = GetEntityComponentData<CM_PathState>(path);
                return state.PathLength;
            }
            var pathDef = GetEntityComponentData<CM_Path>(path);
            var buffer = EntityManager.GetBuffer<CM_PathWaypointElement>(path);
            int count = buffer.Length;
            return math.select(0, math.select(count - 1, count, pathDef.looped), count > 1);
        }

        /// <summary>Call this if the path changes in such a way as to affect distances
        /// or other cached path elements</summary>
        public void InvalidatePathCache(Entity path)
        {
            var state = GetEntityComponentData<CM_PathState>(path);
            state.valid = false;
            EntityManager.SetComponentData(path, state);
        }

        /// <summary>Standardize the unit, so that it lies between MinUmit and MaxUnit</summary>
        /// <param name="path">The entity with the path</param>
        /// <param name="pos">The value to be standardized</param>
        /// <param name="units">The unit type</param>
        /// <returns>The standardized value of pos, between MinUnit and MaxUnit</returns>
        public float ClampUnit(Entity path, float pos, PositionUnits units)
        {
            var pathDef = GetEntityComponentData<CM_Path>(path);
            if (units == PositionUnits.PathUnits)
            {
                var buffer = EntityManager.GetBuffer<CM_PathWaypointElement>(path);
                int count = buffer.Length;
                return ClampValue(pos, math.select(count - 1, count, pathDef.looped), pathDef.looped);
            }
            if (units == PositionUnits.Normalized)
                return ClampValue(pos, 1, pathDef.looped);

            var state = GetEntityComponentData<CM_PathState>(path);
            float len = state.PathLength;
            return ClampValue(pos, len, pathDef.looped);
        }

        /// <summary>Get the path position (in native path units) corresponding to the psovided
        /// value, in the units indicated.
        /// If the distance cache is not valid, then calling this will
        /// trigger a potentially costly regeneration of the path distance cache</summary>
        /// <param name="path">The entity with the path</param>
        /// <param name="pos">The value to convert from</param>
        /// <param name="units">The units in which pos is expressed</param>
        /// <returns>The length of the path in native units, when sampled at this rate</returns>
        public float ToNativePathUnits(Entity path, float pos, PositionUnits units)
        {
            if (units == PositionUnits.PathUnits)
                return pos;

            var pathDef = GetEntityComponentData<CM_Path>(path);
            var state = GetEntityComponentData<CM_PathState>(path);
            float len = state.PathLength;
            if (pathDef.resolution < 1 || len < MathHelpers.Epsilon)
                return 0;

            if (units == PositionUnits.Normalized)
                pos *= state.PathLength;

            // Distance units
            pos = ClampValue(pos, len, pathDef.looped);
            float d = pos / state.p2d2pStep.y;
            int i = (int)math.floor(d);
            if (i >= state.Length-1)
                return MaxUnit(path, PositionUnits.PathUnits);
            float t = d - (float)i;
            return math.lerp(state.p2d2pAt(i).y, state.p2d2pAt(i+1).y, t);
        }

        /// <summary>Get the path position (in path units) corresponding to this distance along the path.
        /// If the distance cache is not valid, then calling this will
        /// trigger a potentially costly regeneration of the path distance cache</summary>
        /// <param name="path">The entity with the path</param>
        /// <param name="pos">The value to convert from, in native units</param>
        /// <param name="units">The units to convert toexpressed</param>
        /// <returns>The length of the path in distance units, when sampled at this rate</returns>
        public float FromPathNativeUnits(Entity path, float pos, PositionUnits units)
        {
            if (units == PositionUnits.PathUnits)
                return pos;

            var pathDef = GetEntityComponentData<CM_Path>(path);
            var state = GetEntityComponentData<CM_PathState>(path);
            float len = state.PathLength;
            if (pathDef.resolution < 1 || len < MathHelpers.Epsilon)
                return 0;

            pos = ClampUnit(path, pos, PositionUnits.PathUnits);
            float d = pos / state.p2d2pStep.x;
            int i = (int)math.floor(d);
            if (i >= state.Length-1)
                pos = len;
            else
                pos = math.lerp(state.p2d2pAt(i).x, state.p2d2pAt(i+1).x, d - (float)i);
            return math.select(pos, pos/len, units == PositionUnits.Normalized);
        }
    }
}
