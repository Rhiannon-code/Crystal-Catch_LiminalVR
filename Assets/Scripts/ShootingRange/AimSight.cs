using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    [RequireComponent(typeof(Pistol))]
    public class AimSight : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private LineRenderer beam;
        [SerializeField] private Transform dot;

        [Header("Aim (data)")]
        [SerializeField] private LayerMask aimMask = ~0;
        [SerializeField] private float maxDistance = 40f;

        [Header("Look (data)")]
        [SerializeField] private bool showBeam = true;
        [SerializeField] private float dotDegrees = 0.5f;
        [SerializeField] private float dotSurfaceLift = 0.005f;

        private Pistol _pistol;

        private void Awake()
        {
            _pistol = GetComponent<Pistol>();
        }

        private void OnDisable()
        {
            if (beam != null) beam.enabled = false;
            if (dot != null) dot.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_pistol == null || !_pistol.IsHeld) { OnDisable(); return; }

            Transform muzzle = _pistol.Muzzle;
            Vector3 origin = muzzle.position;

            RaycastHit hit;
            bool landed = Physics.Raycast(origin, muzzle.forward, out hit, maxDistance, aimMask,
                                          QueryTriggerInteraction.Ignore);
            Vector3 end = landed ? hit.point : origin + muzzle.forward * maxDistance;

            if (beam != null)
            {
                beam.enabled = showBeam;
                beam.SetPosition(0, origin);
                beam.SetPosition(1, end);
            }

            if (dot == null) return;

            dot.gameObject.SetActive(landed);
            if (!landed) return;

            // Same apparent size at any range, or it vanishes at the back wall and blots out a plate up close
            float size = 2f * hit.distance * Mathf.Tan(dotDegrees * 0.5f * Mathf.Deg2Rad);
            dot.position = hit.point + hit.normal * dotSurfaceLift;
            dot.localScale = Vector3.one * size / Mathf.Max(1e-4f, transform.lossyScale.x);
        }

        /// Raycasts at the moment of firing rather than reusing last frame's dot, which would lag a
        /// fast moving hand by a frame
        public Vector3 FireDirection(Vector3 origin, Vector3 forward, float speed, float gravityScale)
        {
            RaycastHit hit;
            if (speed <= 0f || !Physics.Raycast(origin, forward, out hit, maxDistance, aimMask,
                                                QueryTriggerInteraction.Ignore))
                return forward;

            return Solve(hit.point - origin, speed, Physics.gravity * gravityScale, Time.fixedDeltaTime, forward);
        }

        private static Vector3 Solve(Vector3 offset, float speed, Vector3 accel, float dt, Vector3 fallback)
        {
            float distance = offset.magnitude;
            if (distance < 0.01f) return fallback;

            float t = distance / speed;
            Vector3 velocity = offset / t;

            for (int i = 0; i < 5; i++)
            {
                velocity = (offset - 0.5f * accel * t * (t + dt)) / t;
                t *= velocity.magnitude / speed;
            }

            return velocity.sqrMagnitude > 1e-8f ? velocity.normalized : fallback;
        }
    }
}
