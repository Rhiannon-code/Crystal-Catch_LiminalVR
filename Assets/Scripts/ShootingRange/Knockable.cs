using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    [RequireComponent(typeof(Rigidbody))]
    public class Knockable : MonoBehaviour, IShootable
    {
        [Header("Scoring (data)")]
        [SerializeField] private int directHitScore = 25;
        [SerializeField] private int knockdownScore = 100;
        [SerializeField] private int chainBonus = 75;
        [SerializeField] private bool countsTowardRoundGoal = true;

        [Header("Down test (data)")]
        [SerializeField] private float fallAngle = 50f;
        [SerializeField] private float fallDistance = 0.35f;
        [SerializeField] private float settleSeconds = 0.25f;
        [SerializeField] private float shotCreditSeconds = 1.5f;

        [Header("Stack stability (data)")]
        [SerializeField] private int solverIterations = 12;
        [SerializeField] private float sleepThreshold = 0.05f;

        [Header("Collision audio (data)")]
        [SerializeField] private AudioClip[] clatterClips;
        [SerializeField] private float minClatterSpeed = 1.2f;
        [SerializeField] private float loudestClatterSpeed = 6f;
        [SerializeField, Range(0f, 1f)] private float clatterVolume = 0.5f;
        [SerializeField] private float clatterRearmSeconds = 0.08f;

        private static readonly List<Knockable> Registry = new List<Knockable>();
        public static IList<Knockable> All { get { return Registry; } }

        public bool IsDown { get { return _down; } }
        public bool CountsTowardRoundGoal { get { return countsTowardRoundGoal; } }

        private Rigidbody _body;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private Vector3 _restUp;
        private float _settle;
        private float _lastShotTime = float.NegativeInfinity;
        private float _clatterRearmAt;
        private bool _down;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();

            // Physics runs at 33 Hz here
            _body.solverIterations = Mathf.Max(1, solverIterations);
            _body.sleepThreshold = Mathf.Max(0f, sleepThreshold);

            CaptureRestPose();
        }

        private void OnEnable() { Registry.Add(this); }
        private void OnDisable() { Registry.Remove(this); }

        /// Call after a builder or an author has moved this into its final spot
        public void CaptureRestPose()
        {
            _restPosition = transform.position;
            _restRotation = transform.rotation;
            _restUp = transform.up;
        }

        public void ResetToRest()
        {
            if (_body != null)
            {
                _body.velocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            transform.position = _restPosition;
            transform.rotation = _restRotation;

            if (_body != null && !_body.isKinematic) _body.Sleep();

            _down = false;
            _settle = 0f;
            _lastShotTime = float.NegativeInfinity;
        }

        public void OnShot(Vector3 point, Vector3 direction, float impulse)
        {
            if (_down) return;

            _lastShotTime = Time.time;
            Report(directHitScore, point, false);
        }

        private void Update()
        {
            if (_down) return;

            // Nothing that is asleep is falling, and most of the arena is asleep most of the time
            if (_body != null && _body.IsSleeping()) { _settle = 0f; return; }

            bool over = Vector3.Angle(transform.up, _restUp) >= fallAngle ||
                        (transform.position - _restPosition).magnitude >= fallDistance;

            if (!over) { _settle = 0f; return; }

            _settle += Time.deltaTime;
            if (_settle < settleSeconds) return;

            _down = true;

            // Nothing shot it inside the credit window, so something else knocked it over
            bool chained = Time.time - _lastShotTime > shotCreditSeconds;
            Report(knockdownScore + (chained ? chainBonus : 0), transform.position, chained);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Time.time < _clatterRearmAt || clatterClips == null || clatterClips.Length == 0) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed < minClatterSpeed) return;

            _clatterRearmAt = Time.time + clatterRearmSeconds;

            float loudness = Mathf.InverseLerp(minClatterSpeed, loudestClatterSpeed, speed);
            var clip = clatterClips[Random.Range(0, clatterClips.Length)];
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

            if (ImpactFX.Instance != null)
                ImpactFX.Instance.PlayClip(clip, point, clatterVolume * Mathf.Lerp(0.35f, 1f, loudness));
        }

        private static void Report(int points, Vector3 where, bool chained)
        {
            if (points != 0 && RangeGame.Instance != null) RangeGame.Instance.Scored(points, where, chained);
        }
    }
}
