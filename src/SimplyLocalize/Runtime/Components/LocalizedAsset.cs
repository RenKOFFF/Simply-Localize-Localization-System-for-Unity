using UnityEngine;

namespace SimplyLocalize.Components
{
    /// <summary>Custom asset components implement ApplyAsset and ReadCurrentAsset. The base owns the resolved source.</summary>
    public abstract class LocalizedAsset<T> : LocalizedComponentBase where T : Object
    {
        private T original;
        private bool cached;
        private LocalizationSourceLease owner;
        protected abstract void ApplyAsset(T asset);
        protected virtual void RestoreAsset(T asset) => ApplyAsset(asset);
        protected abstract T ReadCurrentAsset();
        protected override void OnEnable() { CacheOriginal(); base.OnEnable(); }
        public override void Refresh()
        {
            CacheOriginal();
            bool found = TryResolveAsset<T>(out var value);
            var next = found ? value.RetainSource() : null;
            var old = owner;
            try { ApplyAsset(found ? (T)value.Asset : original); owner = next; }
            catch
            {
                owner = null;
                try { RestoreAsset(original); }
                finally { ReleaseOwned(next); }
                throw;
            }
            finally { ReleaseOwned(old); }
        }
        protected override void ClearLocalizedValue()
        {
            var old = owner; owner = null;
            try { if (cached) RestoreAsset(original); }
            finally { ReleaseOwned(old); }
        }
        private void CacheOriginal() { if (!cached) { original = ReadCurrentAsset(); cached = true; } }
    }
}
