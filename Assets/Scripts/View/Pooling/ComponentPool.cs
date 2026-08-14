using System;
using System.Collections.Generic;
using UnityEngine;

namespace HanziDefend.View.Pooling
{
    /// <summary>
    /// Bounded component pool for high-frequency presentation objects. Capacity growth is
    /// explicit and observable; after prewarming, Rent/Return do not allocate managed memory.
    /// </summary>
    public sealed class ComponentPool<T> : IDisposable where T : Component
    {
        private readonly Transform parent;
        private readonly Func<Transform, T> factory;
        private readonly Stack<T> available;
        private readonly HashSet<T> rented;
        private readonly int maxRetained;
        private bool disposed;

        public ComponentPool(
            Transform parent,
            Func<Transform, T> factory,
            int prewarmCount,
            int maxRetained)
        {
            this.parent = parent != null
                ? parent
                : throw new ArgumentNullException(nameof(parent));
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));

            if (prewarmCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(prewarmCount),
                    "Prewarm count cannot be negative.");
            }

            if (maxRetained < prewarmCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRetained),
                    "Maximum retained count must be at least the prewarm count.");
            }

            this.maxRetained = maxRetained;
            available = new Stack<T>(maxRetained);
            rented = new HashSet<T>();

            for (int index = 0; index < prewarmCount; index++)
            {
                T item = CreateItem();
                Deactivate(item);
                available.Push(item);
            }
        }

        public int CreatedCount { get; private set; }

        public int ActiveCount => rented.Count;

        public int InactiveCount => available.Count;

        public int MaxRetained => maxRetained;

        public T Rent()
        {
            ThrowIfDisposed();

            T item = null;
            while (available.Count > 0 && item == null)
            {
                item = available.Pop();
            }

            if (item == null)
            {
                item = CreateItem();
            }

            item.transform.SetParent(parent, false);
            item.gameObject.SetActive(true);
            if (!rented.Add(item))
            {
                throw new InvalidOperationException("Pool factory returned an already-rented item.");
            }

            if (item is IPoolableView poolable)
            {
                poolable.OnRentFromPool();
            }

            return item;
        }

        public void Return(T item)
        {
            ThrowIfDisposed();
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (!rented.Remove(item))
            {
                throw new InvalidOperationException(
                    "Only an active item rented from this pool can be returned.");
            }

            if (item is IPoolableView poolable)
            {
                poolable.OnReturnToPool();
            }

            if (available.Count >= maxRetained)
            {
                DestroyObject(item.gameObject);
                return;
            }

            item.transform.SetParent(parent, false);
            Deactivate(item);
            available.Push(item);
        }

        public void ReturnAll()
        {
            ThrowIfDisposed();
            if (rented.Count == 0)
            {
                return;
            }

            var buffer = new T[rented.Count];
            rented.CopyTo(buffer);
            for (int index = 0; index < buffer.Length; index++)
            {
                Return(buffer[index]);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (T item in rented)
            {
                if (item != null)
                {
                    DestroyObject(item.gameObject);
                }
            }

            rented.Clear();
            while (available.Count > 0)
            {
                T item = available.Pop();
                if (item != null)
                {
                    DestroyObject(item.gameObject);
                }
            }
        }

        private T CreateItem()
        {
            T item = factory(parent);
            if (item == null)
            {
                throw new InvalidOperationException("Pool factory returned null.");
            }

            CreatedCount++;
            return item;
        }

        private static void Deactivate(T item)
        {
            if (item.gameObject.activeSelf)
            {
                item.gameObject.SetActive(false);
            }
        }

        private static void DestroyObject(GameObject value)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
    }
}
