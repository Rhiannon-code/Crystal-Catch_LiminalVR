using System;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class FallingMover : MonoBehaviour
    {
        [Header("Feel (data)")]
        [SerializeField] private float spinDegreesPerSecond = 90f;
        [SerializeField] private float maxLife = 12f;

        /// Raised when this object drops past the cart without being hit, a clean miss, no penalty
        public event Action Passed;

        /// Local override on fall speed, multiplied with the session-wide Slow Time scale below
        /// Left at 1 in normal use
        public float SpeedScale { get; set; }

        /// Session wide Slow Time scale, read live so the hazard affects items already in the air
        private float GlobalScale { get { return _game != null ? _game.FallSpeedScale : 1f; } }

        private CrystalCatchGame _game;
        private float _fallSpeed;
        private float _despawnY;
        private float _launchTime;
        private bool _active;

        private void Awake()
        {
            SpeedScale = 1f;
        }

        public static float SpeedForIntercept(float fromY, float toY, float leadSeconds)
        {
            if (leadSeconds <= 0.0001f) return 0f;
            return Mathf.Max(0f, fromY - toY) / leadSeconds;
        }

        /// Begin falling. despawnY is the height below which the object counts as missed
        public void Launch(float fallSpeed, float despawnY, CrystalCatchGame game)
        {
            _fallSpeed = fallSpeed;
            _despawnY = despawnY;
            _game = game;
            _launchTime = Time.time;
            _active = true;
            if (SpeedScale <= 0f) SpeedScale = 1f;
        }

        public void Stop()
        {
            _active = false;
        }

        private void Update()
        {
            if (!_active) return;

            transform.position += Vector3.down * (_fallSpeed * SpeedScale * GlobalScale * Time.deltaTime);

            if (spinDegreesPerSecond != 0f)
                transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.Self);

            // Dropped past the cart, or the safety net caught a stuck object
            if (transform.position.y <= _despawnY || Time.time - _launchTime >= maxLife)
            {
                _active = false;
                var handler = Passed;
                if (handler != null) handler();
            }
        }
    }
}
