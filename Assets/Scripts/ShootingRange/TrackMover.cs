using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    /// A carnival target riding a rail. Kinematic, because a collider that moves without a Rigidbody
    /// makes PhysX rebuild its static tree every step
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

            if (_distance >= _rail.Length) { Retire(); return; }

            _body.MovePosition(_rail.PointAt(_distance));
        }

        public void OnShot(Vector3 point, Vector3 direction, float impulse)
        {
            if (!_running) return;

            _running = false;

            if (ShatterPool.Instance != null)
                ShatterPool.Instance.Burst(transform.position, fragments, direction, fragmentImpulse);

            if (RangeGame.Instance != null) RangeGame.Instance.Scored(hitScore, point, false);

            Retire();
        }

        private void Retire()
        {
            _running = false;
            _rail = null;
            gameObject.SetActive(false);
        }
    }
}
