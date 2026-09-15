using System;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    [RequireComponent(typeof(Rigidbody))]
    public class TrackMover : MonoBehaviour, IShootable
    {
        [Header("Scoring (data)")]
        [SerializeField] private int hitScore = 100;

        [Header("Shape (data, metres)")]
        [SerializeField] private float moverLength = 0.4f;

        [Header("Shatter (data)")]
        [SerializeField] private int fragments = 8;
        [SerializeField] private float fragmentImpulse = 1.4f;

        public float MoverLength { get { return moverLength; } }
        public bool Running { get { return _running; } }

        /// Shot (true), or reached the end of its rail (false)
        public event Action<TrackMover, bool> Resolved;

        private Rigidbody _body;
        private TrackRail _rail;
        private float _speed;
        private float _distance;
        private bool _running;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;

            // Movement is stepped at 33 Hz, without this the targets visibly stutter across the range
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Launch(TrackRail rail, float speed)
        {
            _rail = rail;
            _speed = speed;
            _distance = 0f;
            _running = true;

            transform.position = rail.PointAt(0f);

            // A wall riser runs straight up, and LookRotation with world up as its reference is
            // degenerate there
            Vector3 reference = Mathf.Abs(Vector3.Dot(rail.Direction, Vector3.up)) > 0.99f
                              ? Vector3.forward
                              : Vector3.up;
            transform.rotation = Quaternion.LookRotation(rail.Direction, reference);

            gameObject.SetActive(true);
        }

        private void FixedUpdate()
        {
            if (!_running || _rail == null) return;

            _distance += _speed * Time.fixedDeltaTime;

            if (_distance >= _rail.Length) { ReachEnd(); return; }

            _body.MovePosition(_rail.PointAt(_distance));
        }

        public void OnShot(Vector3 point, Vector3 direction, float impulse)
        {
            if (!_running) return;

            if (ShatterPool.Instance != null)
                ShatterPool.Instance.Burst(transform.position, fragments, direction, fragmentImpulse);

            if (RangeGame.Instance != null) RangeGame.Instance.Scored(hitScore, point, false);

            Resolve(true);
        }

        /// Taken off the range between rounds, which is not an outcome, so nobody hears about it
        public void Stop()
        {
            _running = false;
            _rail = null;
            gameObject.SetActive(false);
        }

        private void ReachEnd()
        {
            if (_rail.ReachesPlayer && RangeGame.Instance != null) RangeGame.Instance.HitPlayer(transform.position);
            Resolve(false);
        }

        private void Resolve(bool shot)
        {
            Stop();
            if (Resolved != null) Resolved(this, shot);
        }
    }
}
