using UnityEngine;
using TMPro;

namespace IntuitiveDesigns.CrystalCatch
{
    public class PerfReadout : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private TMP_Text text;

        [Header("Budget")]
        [SerializeField] private float targetFps = 90f;

        [Header("Sampling")]
        [SerializeField] private float windowSeconds = 0.5f;

        [Header("Native capture")]
        [SerializeField] private bool logEachSample;

        [Header("Display")]
        [SerializeField] private bool showWorstFrame = true;
        [SerializeField] private bool showOverBudgetCount = true;
        [SerializeField] private Color goodColour = new Color(0.55f, 1f, 0.6f);
        [SerializeField] private Color badColour = new Color(1f, 0.45f, 0.4f);
        [SerializeField] private float warnBelowFps = 70f;

        private float _elapsed;
        private int _frames;
        private float _worstMs;
        private int _overBudget;
        private string _lastLine = "-- fps";

        private float BudgetMs { get { return 1000f / Mathf.Max(1f, targetFps); } }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float ms = dt * 1000f;

            _elapsed += dt;
            _frames++;

            if (ms > _worstMs) _worstMs = ms;
            if (ms > BudgetMs) _overBudget++;

            if (_elapsed < windowSeconds) return;

            float fps = _frames / _elapsed;
            float avgMs = (_elapsed * 1000f) / Mathf.Max(1, _frames);

            var sb = new System.Text.StringBuilder(48);
            sb.Append(fps.ToString("0.0")).Append(" fps  ").Append(avgMs.ToString("0.0")).Append(" ms");

            // The number that actually matters. A run can average 72 and still stutter, and the
            // average is exactly what hides it
            if (showWorstFrame) sb.Append("\nworst ").Append(_worstMs.ToString("0.0")).Append(" ms");
            if (showOverBudgetCount) sb.Append("   over ").Append(_overBudget);

            _lastLine = sb.ToString();

            if (text != null)
            {
                text.text = _lastLine;
                text.color = fps < warnBelowFps || _worstMs > BudgetMs * 1.5f ? badColour : goodColour;
            }

            // Deliberately not [Conditional] like the gameplay logs. This one only exists to be read
            // off the device, and it is opt in and off by default
            if (logEachSample) Debug.Log("[Perf] " + _lastLine.Replace("\n", "  "));

            _elapsed = 0f;
            _frames = 0;
            _worstMs = 0f;
            _overBudget = 0;
        }

        /// Current readout as a string, for logging to adb logcat alongside the video
        public string Line { get { return _lastLine; } }
    }
}
