using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    [RequireComponent(typeof(Pistol))]
    public class MouseTestPistol : MonoBehaviour
    {
        [SerializeField] private bool editorOnly = true;

        [Header("Where the gun sits (metres, camera space)")]
        [SerializeField] private Vector3 viewOffset = new Vector3(0.22f, -0.20f, 0.34f);

        [Header("Aim")]
        [SerializeField] private LayerMask aimMask = ~0;
        [SerializeField] private float maxAimDistance = 60f;
        [SerializeField] private float fallbackAimDistance = 8f;
        [SerializeField] private float scrollSpeed = 2f;

        private Camera _cam;
        private Pistol _pistol;

        private void Awake()
        {
            _pistol = GetComponent<Pistol>();
        }

        private void OnDisable()
        {
            if (_pistol != null) _pistol.ExternallyDriven = false;
        }

        private bool Active { get { return !editorOnly || Application.isEditor; } }

        private void Update()
        {
            if (!Active || _pistol == null) return;

            if (!_pistol.IsHeld)
            {
                _pistol.ExternallyDriven = false;
                return;
            }

            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) _cam = Object.FindObjectOfType<Camera>();
                if (_cam == null) return;
            }

            _pistol.ExternallyDriven = true;

            fallbackAimDistance = Mathf.Clamp(
                fallbackAimDistance + Input.GetAxis("Mouse ScrollWheel") * scrollSpeed, 2f, maxAimDistance);

            transform.position = _cam.transform.TransformPoint(viewOffset);

            Vector3 toAim = AimPoint() - transform.position;
            if (toAim.sqrMagnitude > 1e-5f)
                transform.rotation = Quaternion.LookRotation(toAim.normalized, Vector3.up);
        }

        /// What the cursor is actually over, so the muzzle converges there instead of running
        /// parallel to the view and drifting off by the width of the offset
        private Vector3 AimPoint()
        {
            var ray = _cam.ScreenPointToRay(Input.mousePosition);

            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, maxAimDistance, aimMask, QueryTriggerInteraction.Ignore))
                return hit.point;

            return ray.GetPoint(fallbackAimDistance);
        }
    }
}
