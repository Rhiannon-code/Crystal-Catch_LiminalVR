using System;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class FallingMover : MonoBehaviour
    {
        [Header("Feel (data)")]
        [SerializeField] private float spinDegreesPerSecond = 90f;
        [SerializeField] private float maxLife = 12f;

        [Header("Trail")]
        [SerializeField] private TrailRenderer trail;

        [Header("Portal hold")]
        [SerializeField] private Renderer[] hiddenWhileHeld;

        public event Action Passed;
        public float SpeedScale { get; set; }
        private float GlobalScale { get { return _game != null ? _game.FallSpeedScale : 1f; } }
        private CrystalCatchGame _game;
        private float _fallSpeed;
        private float _despawnY;
        private float _launchTime;
        private float _hold;
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
            Launch(fallSpeed, despawnY, game, 0f);
        }

        /// holdSeconds keeps the item hidden and still at the spawn point while its portal opens,
        /// so it reads as coming THROUGH the portal rather than appearing beside it
        public void Launch(float fallSpeed, float despawnY, CrystalCatchGame game, float holdSeconds)
        {
            Launch(fallSpeed, despawnY, game, holdSeconds, true);
        }

        public void Launch(float fallSpeed, float despawnY, CrystalCatchGame game, float holdSeconds,
                           bool hideWhileHeld)
        {
            _fallSpeed = fallSpeed;
            _despawnY = despawnY;
            _game = game;
            _launchTime = Time.time;
            _hold = Mathf.Max(0f, holdSeconds);
            _active = true;
            if (SpeedScale <= 0f) SpeedScale = 1f;

            // Before the trail is cleared, or Clear() itself leaves one segment at the old position
            SetHidden(_hold > 0f && hideWhileHeld);
            ClearTrail();
        }

        public void Stop()
        {
            _active = false;
            if (trail != null) trail.emitting = false;
        }

        private void ClearTrail()
        {
            if (trail == null) return;
            trail.Clear();
            // Held items must not emit yet, a trail from a stationary hidden object is a blob
            trail.emitting = _hold <= 0f;
        }

        private void SetHidden(bool hidden)
        {
            if (hiddenWhileHeld == null) return;
            for (int i = 0; i < hiddenWhileHeld.Length; i++)
                if (hiddenWhileHeld[i] != null) hiddenWhileHeld[i].enabled = !hidden;
        }

        private void Update()
        {
            if (!_active) return;

            // Waiting in the portal. Still counts against maxLife, so a stuck item cannot leak
            if (_hold > 0f)
            {
                _hold -= Time.deltaTime;
                if (_hold > 0f) return;

                _hold = 0f;
                SetHidden(false);
                ClearTrail();          // Wmitting starts HERE, at the portal mouth
            }

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
