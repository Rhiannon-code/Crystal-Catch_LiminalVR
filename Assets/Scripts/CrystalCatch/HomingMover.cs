using System;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class HomingMover : MonoBehaviour
    {
        [Header("Feel (data: tuned for Energy, not chaos)")]
        [SerializeField] private float speed = 1.1f;          // M/s approachm catchable but reactive
        [SerializeField] private float homingStrength = 2.5f; // How hard it steers toward the target
        [SerializeField] private float maxLife = 6f;          // Safety despawn
        [SerializeField] private float passDistance = 0.12f;  // Within this of target counts as "arrived"

        /// Raised when the mover reaches/passes the player without being caught
        public event Action Passed;

        private Transform _target;
        private Vector3 _velocity;
        private float _launchTime;
        private bool _active;

        /// Launch toward target. startDir seeds the initial heading so crystals fan out
        public void Launch(Transform target, Vector3 startDir, float? speedOverride = null)
        {
            _target = target;
            float s = speedOverride ?? speed;
            _velocity = startDir.normalized * s;
            _launchTime = Time.time;
            _active = true;
        }

        public void Stop() => _active = false;

        private void Update()
        {
            if (!_active) return;

            if (_target != null)
            {
                Vector3 toTarget = (_target.position - transform.position);
                float dist = toTarget.magnitude;

                // Arrived at/passed the player's catch zone -> a miss
                bool passedPlane = Vector3.Dot(toTarget, _velocity) < 0f; // Target now behind our heading
                if (dist <= passDistance || passedPlane)
                {
                    _active = false;
                    Passed?.Invoke();
                    return;
                }

                // Gentle steer toward the target, keeping constant speed
                Vector3 desired = toTarget.normalized * _velocity.magnitude;
                _velocity = Vector3.Lerp(_velocity, desired, homingStrength * Time.deltaTime);
            }

            transform.position += _velocity * Time.deltaTime;
            transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.Self); // Gentle spin, cosmetic

            if (Time.time - _launchTime >= maxLife)
            {
                _active = false;
                Passed?.Invoke();
            }
        }
    }
}
