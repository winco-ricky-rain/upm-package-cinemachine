using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Entities;
using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Cinemachine.ECS
{
    internal unsafe struct CM_BlendLookup
    {
        public struct BlendDef
        {
            public BlendCurve curve;
            public float duration;
        }
        struct BlendListItem
        {
            public Entity from;
            public Entity to;
            public BlendDef def;
        }
        BlendListItem* blends;

        public int Capacity { get; private set; }
        public int Length { get; private set; }
        int growStep;

        public void Dispose()
        {
            if (blends != null)
                UnsafeUtility.Free(blends, Allocator.Persistent);
            blends = null;
            Capacity = 0;
            Length = 0;
        }

        public void Reset(int growStep)
        {
            Dispose();
            this.growStep = growStep;
            GrowBuffer();
        }

        void GrowBuffer()
        {
            int itemSize = sizeof(BlendListItem);
            int capacity = Capacity + Math.Max(1, growStep);
            var oldBlends = blends;
            blends = (BlendListItem*)UnsafeUtility.Malloc(
                itemSize * capacity,
                UnsafeUtility.AlignOf<BlendListItem>(), Allocator.Persistent);
            UnsafeUtility.MemClear(blends + itemSize * Capacity, itemSize * (capacity - Capacity));
            if (Length > 0)
            {
                UnsafeUtility.MemCpy(blends, oldBlends, Length * itemSize);
                UnsafeUtility.Free(oldBlends, Allocator.Persistent);
            }
            Capacity = capacity;
        }

        public void AddBlendToLookup(Entity from, Entity to, BlendDef def)
        {
            if (Capacity <= Length)
                GrowBuffer();
            blends[Length++] = new BlendListItem { from = from, to = to, def = def };
        }

        public BlendDef LookupBlend(Entity from, Entity to, BlendDef defaultBlend)
        {
            int fromToAny = -1;
            int toToAny = -1;
            int anyToAny = -1;
            for (int i = 0; i < Length; ++i)
            {
                var item = blends[i];
                if (item.from == from && item.to == to)
                    return item.def;
                if (item.from == from && item.to == Entity.Null)
                    fromToAny = i;
                else if (item.from == Entity.Null && item.to == to)
                    toToAny = i;
                else if (item.from == Entity.Null && item.to == Entity.Null)
                    anyToAny = i;
            }
            if (toToAny >= 0)
                return blends[toToAny].def;
            if (fromToAny >= 0)
                return blends[fromToAny].def;
            if (anyToAny >= 0)
                return blends[anyToAny].def;
            return defaultBlend;
        }
    }
}
