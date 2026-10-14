namespace LOMSave.Probe
{
    internal sealed class DialoguePacer
    {
        private int _version = -1;
        private float _elapsed;
        private float _fullyVisible;
        private bool _advanced;

        internal bool Tick(int version, bool enabled, bool ready, float delta, float delay)
        {
            if (_version != version || !enabled)
            {
                _version = version;
                _elapsed = 0f;
                _fullyVisible = 0f;
                _advanced = false;
            }
            if (!enabled || _advanced) return false;
            _elapsed += delta;
            _fullyVisible = ready ? _fullyVisible + delta : 0f;
            if (!ready || _fullyVisible < System.Math.Min(0.35f, delay * 0.25f) || _elapsed < delay) return false;
            _advanced = true;
            return true;
        }
    }
}
