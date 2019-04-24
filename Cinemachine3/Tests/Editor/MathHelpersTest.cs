using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using System.Collections;
using Unity.Mathematics;

namespace Cinemachine.ECS
{
    [TestFixture]
    public class MathHelpersTest
    {
        [Test]
	    public void AlmostZero()
        {
            Assert.That(new float3(0, 0, 0).AlmostZero());
            Assert.That(new float3(0, 0, -0.0001f).AlmostZero());
            Assert.That(new float3(-0.0001f, 0.0001f, -0.0001f).AlmostZero());

            Assert.That(!new float3(0, 0, 0.001f).AlmostZero());
            Assert.That(!new float3(-0.001f, 0.001f, 0.001f).AlmostZero());
            Assert.That(!new float3(0.1f, 0.01f, 0).AlmostZero());
        }

        [Test]
	    public void ProjectOntoPlane()
        {
            var d1 = math.normalize(new float3(1, 2, 3));
            var d2 = math.normalize(new float3(2, 3, 1));
            Assert.That(MathHelpers.ProjectOntoPlane(d1, d1).AlmostZero());
            var v = MathHelpers.ProjectOntoPlane(d1, d2);
            Assert.That(!v.AlmostZero());
            Assert.That(math.length(v) < 1);
            Assert.That(math.abs(math.dot(v, d2)) < MathHelpers.Epsilon);
            Assert.That(!math.cross(v, d2).AlmostZero());
        }

        [Test]
	    public void DampFloat()
        {
            const float dampTime = 10f;
            const float initial = 100f;
            float[] fixedFactor = new float[3] { 0.79f, 0f, 1.07f };
            for (int f = 0; f < fixedFactor.Length; ++f)
            {
                float t = 0;
                float r = MathHelpers.Damp(initial, dampTime, t);
                Assert.AreEqual(0, r);
                Assert.Less(r, initial);
                const int iterations = 10;
                for (int i = 0; i < iterations; ++i)
                {
                    t += dampTime / iterations;
                    float fdt = fixedFactor[f] * t;
                    string msg = "i = " + i + ", t = " + t + ", fdt = " + fdt;
                    if (i != iterations-1)
                        Assert.Less(t, dampTime, msg);
                    else
                        t = dampTime;
                    float r2 = MathHelpers.Damp(initial, dampTime, t, fdt);
                    Assert.Less(r, r2, msg);
                    r = r2;
                }
                //Assert.AreEqual(initial * (1 - MathHelpers.kNegligibleResidual), r, "f = " + f);
            }
	    }

        [Test]
	    public void DampFloat3()
        {
            float3 dampTime = new float3(10f, 9f, 8f);
            float3 initial = new float3(100f, 110f, 120f);
            float[] fixedFactor = new float[3] { 0.79f, 0f, 1.07f };
            for (int f = 0; f < fixedFactor.Length; ++f)
            {
                float t = 0;
                float3 r = MathHelpers.Damp(initial, dampTime, t);
                Assert.AreEqual(0, r.x);
                Assert.AreEqual(0, r.y);
                Assert.AreEqual(0, r.z);
                const int iterations = 10;
                for (int i = 0; i < iterations; ++i)
                {
                    t += math.cmax(dampTime) / iterations;
                    float fdt = fixedFactor[f] * t;
                    string msg = "i = " + i + ", t = " + t + ", fdt = " + fdt;
                    if (i != iterations-1)
                        Assert.Less(t, math.cmax(dampTime), msg);
                    else
                        t = math.cmax(dampTime);
                    float3 r2 = MathHelpers.Damp(initial, dampTime, t, fdt);
                    Assert.LessOrEqual(r.x, r2.x, msg);
                    Assert.LessOrEqual(r.y, r2.y, msg);
                    Assert.LessOrEqual(r.z, r2.z, msg);
                    r = r2;
                }
                //Assert.AreEqual(initial * (1 - MathHelpers.kNegligibleResidual), r, "f = " + f);
            }
	    }

        [Test]
	    public void DampFloat2()
        {
            float2 dampTime = new float2(10f, 9f);
            float2 initial = new float2(100f, 110f);
            float[] fixedFactor = new float[3] { 0.79f, 0f, 1.07f };
            for (int f = 0; f < fixedFactor.Length; ++f)
            {
                float t = 0;
                float2 r = MathHelpers.Damp(initial, dampTime, t);
                Assert.AreEqual(0, r.x);
                Assert.AreEqual(0, r.y);
                const int iterations = 10;
                for (int i = 0; i < iterations; ++i)
                {
                    t += math.cmax(dampTime) / iterations;
                    float fdt = fixedFactor[f] * t;
                    string msg = "i = " + i + ", t = " + t + ", fdt = " + fdt;
                    if (i != iterations-1)
                        Assert.Less(t, math.cmax(dampTime), msg);
                    else
                        t = math.cmax(dampTime);
                    float2 r2 = MathHelpers.Damp(initial, dampTime, t, fdt);
                    Assert.LessOrEqual(r.x, r2.x, msg);
                    Assert.LessOrEqual(r.y, r2.y, msg);
                    r = r2;
                }
                //Assert.AreEqual(initial * (1 - MathHelpers.kNegligibleResidual), r, "f = " + f);
            }
	    }

#if false
	    // A UnityTest behaves like a coroutine in PlayMode
	    // and allows you to yield null to skip a frame in EditMode
	    [UnityTest]
	    public IEnumerator PlayModeSampleTestWithEnumeratorPasses()
        {
		    // Use the Assert class to test conditions.
		    // yield to skip a frame
		    yield return null;
	    }
#endif
    }
}

