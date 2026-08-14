using System;
using System.Collections.Generic;
using UnityEngine;

namespace HanziDefend.View.Pooling
{
    public readonly struct SpriteFxRequest
    {
        public SpriteFxRequest(
            Sprite sprite,
            Vector3 position,
            float lifetimeSeconds,
            Color startColor,
            Color endColor,
            Vector2 startScale,
            Vector2 endScale,
            int sortingOrder,
            float rotationDegrees = 0f)
        {
            if (float.IsNaN(lifetimeSeconds)
                || float.IsInfinity(lifetimeSeconds)
                || lifetimeSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lifetimeSeconds),
                    "FX lifetime must be finite and positive.");
            }

            Sprite = sprite;
            Position = position;
            LifetimeSeconds = lifetimeSeconds;
            StartColor = startColor;
            EndColor = endColor;
            StartScale = startScale;
            EndScale = endScale;
            SortingOrder = sortingOrder;
            RotationDegrees = rotationDegrees;
        }

        public Sprite Sprite { get; }
        public Vector3 Position { get; }
        public float LifetimeSeconds { get; }
        public Color StartColor { get; }
        public Color EndColor { get; }
        public Vector2 StartScale { get; }
        public Vector2 EndScale { get; }
        public int SortingOrder { get; }
        public float RotationDegrees { get; }
    }

    /// <summary>
    /// Centralized FX pool. The owning View advances it once per presentation frame;
    /// pooled instances never register their own Update callbacks.
    /// </summary>
    public sealed class SpriteFxPool : IDisposable
    {
        private readonly ComponentPool<PooledSpriteFx> pool;
        private readonly List<PooledSpriteFx> active;
        private bool disposed;

        public SpriteFxPool(Transform parent, int prewarmCount, int maxRetained)
        {
            active = new List<PooledSpriteFx>(maxRetained);
            pool = new ComponentPool<PooledSpriteFx>(
                parent,
                CreateInstance,
                prewarmCount,
                maxRetained);
        }

        public int CreatedCount => pool.CreatedCount;

        public int ActiveCount => active.Count;

        public int InactiveCount => pool.InactiveCount;

        public PooledSpriteFx Play(SpriteFxRequest request)
        {
            ThrowIfDisposed();
            PooledSpriteFx instance = pool.Rent();
            instance.Initialize(request);
            active.Add(instance);
            return instance;
        }

        public void Advance(float deltaTimeSeconds)
        {
            ThrowIfDisposed();
            if (float.IsNaN(deltaTimeSeconds)
                || float.IsInfinity(deltaTimeSeconds)
                || deltaTimeSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaTimeSeconds),
                    "FX delta time must be finite and non-negative.");
            }

            for (int index = active.Count - 1; index >= 0; index--)
            {
                PooledSpriteFx instance = active[index];
                if (instance.Advance(deltaTimeSeconds))
                {
                    RecycleAt(index);
                }
            }
        }

        public void RecycleAll()
        {
            ThrowIfDisposed();
            for (int index = active.Count - 1; index >= 0; index--)
            {
                RecycleAt(index);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            active.Clear();
            pool.Dispose();
        }

        private static PooledSpriteFx CreateInstance(Transform parent)
        {
            var gameObject = new GameObject("Pooled Sprite FX");
            gameObject.transform.SetParent(parent, false);
            return gameObject.AddComponent<PooledSpriteFx>();
        }

        private void RecycleAt(int index)
        {
            int lastIndex = active.Count - 1;
            PooledSpriteFx instance = active[index];
            active[index] = active[lastIndex];
            active.RemoveAt(lastIndex);
            pool.Return(instance);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SpriteFxPool));
            }
        }
    }
}
