using System;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class SlowMotion : MonoBehaviour
    {
        public static SlowMotion Instance { get; private set; }

        [SerializeField] private float blendSeconds = 0.15f;

        public bool Active { get; private set; }

        public event Action<bool> Changed;

        private float _baseFixedDeltaTime;
        private float _target = 1f;

        private void Awake()
        {
            Instance = this;
            _baseFixedDeltaTime = Time.fixedDeltaTime;
        }

        private void OnDisable()
        {
            Restore(false);
        }

        private void OnDestroy()
        {
            Restore(false);
            if (Instance == this) Instance = null;
        }

        public void Enter(float scale)
        {
            _target = Mathf.Clamp(scale, 0.05f, 1f);
            if (Active) return;

            Active = true;
            if (Changed != null) Changed(true);
        }

        public void Exit()
        {
            _target = 1f;
            if (!Active) return;

            Active = false;
            if (Changed != null) Changed(false);
        }

        public void Restore()
        {
            Restore(true);
        }

        private void Restore(bool raise)
        {
            bool wasActive = Active;

            _target = 1f;
            Active = false;
            Apply(1f);

            if (raise && wasActive && Changed != null) Changed(false);
        }

        private void Update()
        {
            float current = Time.timeScale;
            if (Mathf.Approximately(current, _target)) return;

            float step = blendSeconds > 0f ? Time.unscaledDeltaTime / blendSeconds : 1f;
            Apply(Mathf.MoveTowards(current, _target, step));
        }

        private void Apply(float scale)
        {
            Time.timeScale = scale;

            // Unity's own guidance, without this the physics step stays fixed in game time, so props
            // update at a third of their normal rate and visibly judder through the slow down
            Time.fixedDeltaTime = _baseFixedDeltaTime * scale;
        }
    }
}
