using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Cinemachine3
{
    public static class BezierHelpers
    {
        public static unsafe void ComputeSmoothControlPoints(
            float4* knot, float4* ctrl1, float4* ctrl2, float4* scratch, int numPoints)
        {
            if (numPoints <= 2)
            {
                if (numPoints == 2)
                {
                    ctrl1[0] = math.lerp(knot[0], knot[1], 0.33333f);
                    ctrl2[0] = math.lerp(knot[0], knot[1], 0.66666f);
                }
                else if (numPoints == 1)
                    ctrl1[0] = ctrl2[0] = knot[0];
                return;
            }

            int n = numPoints - 1;
            float4* abcr = scratch;
            for (int axis = 0; axis < 4; ++axis)
            {
                // Linear into the first segment
                abcr[0] = new float4(0, 2, 1, knot[0][axis] + 2 * knot[1][axis]);

                // Internal segments
                for (int i = 1; i < n - 1; ++i)
                    abcr[i] = new float4(1, 4, 1, 4 * knot[i][axis] + 2 * knot[i+1][axis]);

                // Linear out of the last segment
                abcr[n - 1] = new float4(2, 7, 0, 8 * knot[n - 1][axis] + knot[n][axis]);

                // Solve with Thomas algorithm
                for (int i = 1; i < n; ++i)
                {
                    float m = abcr[i].x / abcr[i-1].y;
                    abcr[i] = new float4(
                        abcr[i].x, abcr[i].y - m * abcr[i-1].z,
                        abcr[i].z, abcr[i].w - m * abcr[i-1].w);
                }

                // Compute ctrl1
                var c = ctrl1[n-1];
                c[axis] = abcr[n-1].w / abcr[n-1].y;
                ctrl1[n-1] = c;
                for (int i = n - 2; i >= 0; --i)
                {
                    c = ctrl1[i];
                    c[axis] = (abcr[i].w - abcr[i].z * ctrl1[i + 1][axis]) / abcr[i].y;
                    ctrl1[i] = c;
                }

                // Compute ctrl2 from ctrl1
                for (int i = 0; i < n; i++)
                {
                    c = ctrl2[i];
                    c[axis] = 2 * knot[i + 1][axis] - ctrl1[i + 1][axis];
                    ctrl2[i] = c;
                }
                c = ctrl2[n - 1];
                c[axis] = 0.5f * (knot[n][axis] + ctrl1[n - 1][axis]);
                ctrl2[n - 1] = c;
            }
        }

        public static unsafe void ComputeSmoothControlPointsLooped(
            NativeArray<float4> knot, NativeArray<float4> ctrl1, NativeArray<float4> ctrl2)
        {
            int numPoints = knot.Length;
            if (numPoints < 2)
            {
                if (numPoints == 1)
                    ctrl1[0] = ctrl2[0] = knot[0];
                return;
            }

            int margin = math.min(4, numPoints-1);
            int arraySize = numPoints + 2 * margin;
            NativeArray<float4> knotLooped  = new NativeArray<float4>(arraySize, Allocator.Temp);
            NativeArray<float4> ctrl1Looped = new NativeArray<float4>(arraySize, Allocator.Temp);
            NativeArray<float4> ctrl2Looped = new NativeArray<float4>(arraySize, Allocator.Temp);
            NativeArray<float4> scratch = new NativeArray<float4>(arraySize, Allocator.Temp);
            for (int i = 0; i < margin; ++i)
            {
                knotLooped[i] = knot[numPoints-(margin-i)];
                knotLooped[numPoints+margin+i] = knot[i];
            }
            for (int i = 0; i < numPoints; ++i)
                knotLooped[i + margin] = knot[i];
            ComputeSmoothControlPoints(
                (float4*)knotLooped.GetUnsafePtr(),
                (float4*)ctrl1Looped.GetUnsafePtr(),
                (float4*)ctrl2Looped.GetUnsafePtr(),
                (float4*)scratch.GetUnsafePtr(), knotLooped.Length);
            for (int i = 0; i < numPoints; ++i)
            {
                ctrl1[i] = ctrl1Looped[i + margin];
                ctrl2[i] = ctrl2Looped[i + margin];
            }
            knotLooped.Dispose();
            ctrl1Looped.Dispose();
            ctrl2Looped.Dispose();
            scratch.Dispose();
        }
    }
}
