using UnityEngine;

namespace HanziDefend.View.Pooling
{
    /// <summary>
    /// One centrally-ticked Sprite FX instance. It intentionally has no Update method.
    /// </summary>
    public sealed class PooledSpriteFx : MonoBehaviour, IPoolableView
    {
        private SpriteRenderer spriteRenderer;
        private float elapsedSeconds;
        private float lifetimeSeconds;
        private Color startColor;
        private Color endColor;
        private Vector2 startScale;
        private Vector2 endScale;

        public SpriteRenderer Renderer => spriteRenderer;

        public void Initialize(SpriteFxRequest request)
        {
            EnsureRenderer();
            elapsedSeconds = 0f;
            lifetimeSeconds = request.LifetimeSeconds;
            startColor = request.StartColor;
            endColor = request.EndColor;
            startScale = request.StartScale;
            endScale = request.EndScale;

            transform.position = request.Position;
            transform.localRotation = Quaternion.Euler(0f, 0f, request.RotationDegrees);
            transform.localScale = new Vector3(startScale.x, startScale.y, 1f);
            spriteRenderer.sprite = request.Sprite;
            spriteRenderer.color = startColor;
            spriteRenderer.sortingOrder = request.SortingOrder;
            spriteRenderer.enabled = request.Sprite != null;
        }

        public bool Advance(float deltaTimeSeconds)
        {
            elapsedSeconds = Mathf.Min(lifetimeSeconds, elapsedSeconds + deltaTimeSeconds);
            float progress = lifetimeSeconds <= 0f
                ? 1f
                : Mathf.Clamp01(elapsedSeconds / lifetimeSeconds);
            spriteRenderer.color = Color.LerpUnclamped(startColor, endColor, progress);
            Vector2 scale = Vector2.LerpUnclamped(startScale, endScale, progress);
            transform.localScale = new Vector3(scale.x, scale.y, 1f);
            return elapsedSeconds >= lifetimeSeconds;
        }

        public void OnRentFromPool()
        {
            EnsureRenderer();
        }

        public void OnReturnToPool()
        {
            elapsedSeconds = 0f;
            lifetimeSeconds = 0f;
            spriteRenderer.sprite = null;
            spriteRenderer.enabled = false;
        }

        private void EnsureRenderer()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                {
                    spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
                }
            }
        }
    }
}
