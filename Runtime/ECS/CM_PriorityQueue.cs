using Unity.Collections;
using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Cinemachine.ECS
{
    public unsafe struct CM_PriorityQueue
    {
        public struct QueueEntry
        {
            public Entity entity;
            public CM_VcamPriority vcamPriority;
            public CM_VcamShotQuality shotQuality;
        }

        QueueEntry* data;
        int reserved;
        int length;

        public int Capacity { get; private set; }
        public int Length { get { return length; } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity EntityAt(int index)
        {
            return index >= 0 && index < length ? data[index].entity : Entity.Null;
        }

        // GML: Why can't this be called from burst-compiled jobs?!?!?
        public void Sort(IComparer<QueueEntry> comparer)
        {
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<QueueEntry>(
                data, length, Allocator.None);

            #if ENABLE_UNITY_COLLECTIONS_CHECKS
                var safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, safety);
            #endif

            array.Sort(comparer);

            #if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.Release(safety);
            #endif
        }
        // GML Use this hack instead:
        public void* GetUnsafeDataPtr() { return data; }

        // Call outside of job
        public void ResetReserved()
        {
            reserved = 0;
            length = 0;
        }

        // Call from job
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InterlockedIncrementReserved()
        {
            Interlocked.Increment(ref reserved);
        }

        // Call outside of job
        public void AllocateReservedQueue()
        {
            int itemSize = sizeof(QueueEntry);
            if (Capacity < reserved)
            {
                Capacity = reserved;
                if (data != null)
                    UnsafeUtility.Free(data, Allocator.Persistent);
                data = (QueueEntry*)UnsafeUtility.Malloc(
                    itemSize * Capacity,
                    UnsafeUtility.AlignOf<QueueEntry>(), Allocator.Persistent);
            }
            reserved = 0;
            length = 0;
        }

        // Call from job
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InterlockedAddItem(QueueEntry item)
        {
            data[Interlocked.Increment(ref length)-1] = item;
        }

        // Call outside of job
        public void Dispose()
        {
            if (data != null)
                UnsafeUtility.Free(data, Allocator.Persistent);
            data = null;
            Capacity = 0;
            length = 0;
            reserved = 0;
        }
    }
}

