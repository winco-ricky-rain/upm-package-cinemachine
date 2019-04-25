using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Unity.Cinemachine3
{
    [Serializable]
    public struct BlendCurve
    {
        [Range(0, 1)]
        public float A;

        [Range(0, 1)]
        public float B;

        [Range(-1, 1)]
        public float bias;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float Evaluate(float t)
        {
            t = MathHelpers.Bias(t, (1f - bias) * 0.5f);
            return MathHelpers.Bezier(t, 0, A, 1 - B, 1);
        }

        public static BlendCurve Default { get { return new BlendCurve{ A = 0, B = 0, bias = 0 }; } }
        public static BlendCurve Linear { get { return new BlendCurve{ A = 0.3333f, B = 0.3333f, bias = 0 }; } }
    }
}
