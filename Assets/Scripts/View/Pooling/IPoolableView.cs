namespace HanziDefend.View.Pooling
{
    /// <summary>
    /// Optional lifecycle hooks for components managed by ComponentPool.
    /// </summary>
    public interface IPoolableView
    {
        void OnRentFromPool();

        void OnReturnToPool();
    }
}
