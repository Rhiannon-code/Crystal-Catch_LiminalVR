using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class Projectile : MonoBehaviour
    {
        [Header("Flight (data)")]
        [SerializeField] private float speed = 38f;
        [SerializeField] private float gravityScale = 0.35f;
        [SerializeField] private float radius = 0.02f;
        [SerializeField] private float maxLifetime = 4f;
        [SerializeField] private float maxRange = 60f;

        [Header("Impact (data)")]
        [SerializeField] private float impulse = 6f;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private float upwardBias = 0.15f;

        public bool Live { get { return _live; } }
        public float Impulse { get { return impulse; } }
        public float Speed { get { return speed; } }
        public float GravityScale { get { return gravityScale; } }

        private TrailRenderer _trail;
        private Vector3 _velocity;
        private float _distance;
        private float _age;
        private bool _live;

        private void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
        }

        public void Fire(Vector3 origin, Vector3 direction, float speedScale)
        {
            transform.position = origin;
            _velocity = direction.normalized * speed * Mathf.Max(0.05f, speedScale);
            _age = 0f;
            _distance = 0f;
            _live = true;
            AimAlongVelocity();
            gameObject.SetActive(true);

            // Or it draws a streak from wherever this round last died
            if (_trail != null) _trail.Clear();
        }

        private void FixedUpdate()
        {
            if (!_live) return;

            _age += Time.fixedDeltaTime;
            if (_age >= maxLifetime || _distance >= maxRange) { Finish(false, Vector3.zero, Vector3.up); return; }

            _velocity += Physics.gravity * gravityScale * Time.fixedDeltaTime;

            Vector3 step = _velocity * Time.fixedDeltaTime;
            float length = step.magnitude;
            if (length <= 0f) return;

            RaycastHit hit;
            if (Physics.SphereCast(transform.position, radius, step / length, out hit, length,
                                   hitMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                Impact(hit);
                return;
            }

            transform.position += step;
            _distance += length;
            AimAlongVelocity();
        }

        private void Impact(RaycastHit hit)
        {
            Vector3 direction = _velocity.sqrMagnitude > 1e-6f ? _velocity.normalized : transform.forward;

            var body = hit.rigidbody;
            if (body != null && !body.isKinematic)
            {
                // Dead flat, the same impulse only slides a prop along the bench
                Vector3 push = (direction + Vector3.up * upwardBias).normalized * impulse;
                body.AddForceAtPosition(push, hit.point, ForceMode.Impulse);
            }

            var shootable = hit.collider.GetComponentInParent<IShootable>();
            if (shootable != null) shootable.OnShot(hit.point, direction, impulse);

            Finish(true, hit.point, hit.normal);
        }

        private void Finish(bool struck, Vector3 point, Vector3 normal)
        {
            _live = false;
            gameObject.SetActive(false);

            if (struck && ImpactFX.Instance != null) ImpactFX.Instance.PlayImpact(point, normal);
            if (ProjectilePool.Instance != null) ProjectilePool.Instance.Return(this);
        }

        private void AimAlongVelocity()
        {
            if (_velocity.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(_velocity);
        }
    }
}
