using System;
using HanziDefend.View.Pooling;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;

namespace HanziDefend.Tests.PlayMode.Performance
{
    [TestFixture]
    public sealed class PoolingPerformanceTests
    {
        private const int PrewarmCount = 8;
        private const int MeasurementCycles = 256;

        [Test, Performance]
        public void SpriteFxPool_WarmedPlayAndRecycle_AllocatesZeroManagedBytes()
        {
            var root = new GameObject("FX Pool Performance Root");
            Texture2D texture = null;
            Sprite sprite = null;
            SpriteFxPool pool = null;
            try
            {
                texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                texture.SetPixel(0, 0, Color.white);
                texture.Apply(false, true);
                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 1f, 1f),
                    new Vector2(0.5f, 0.5f),
                    1f);
                pool = new SpriteFxPool(root.transform, PrewarmCount, PrewarmCount);
                var request = new SpriteFxRequest(
                    sprite,
                    Vector3.zero,
                    0.25f,
                    Color.white,
                    Color.clear,
                    Vector2.one,
                    Vector2.one * 1.5f,
                    100);

                for (int warmup = 0; warmup < PrewarmCount * 2; warmup++)
                {
                    pool.Play(request);
                    pool.Advance(request.LifetimeSeconds);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int cycle = 0; cycle < MeasurementCycles; cycle++)
                {
                    pool.Play(request);
                    pool.Advance(request.LifetimeSeconds);
                }

                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Measure.Custom(
                    new SampleGroup("SpriteFxPool.SteadyGcAlloc", SampleUnit.Byte, false),
                    allocatedBytes);
                TestContext.WriteLine(
                    $"Sprite FX pool steady GC: {allocatedBytes} B across "
                    + $"{MeasurementCycles} play/recycle cycles.");

                Assert.That(pool.CreatedCount, Is.EqualTo(PrewarmCount));
                Assert.That(pool.ActiveCount, Is.Zero);
                Assert.That(pool.InactiveCount, Is.EqualTo(PrewarmCount));
                Assert.That(allocatedBytes, Is.Zero,
                    "A prewarmed FX play/recycle cycle must not allocate managed memory.");
            }
            finally
            {
                pool?.Dispose();
                if (sprite != null)
                {
                    UnityEngine.Object.Destroy(sprite);
                }

                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }

                UnityEngine.Object.Destroy(root);
            }
        }
    }
}
