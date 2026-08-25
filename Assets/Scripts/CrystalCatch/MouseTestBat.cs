using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    [RequireComponent(typeof(BatSwinger))]
    public class MouseTestBat : MonoBehaviour
    {
        [SerializeField] private bool editorOnly = true;

        [Header("Reach (scroll wheel)")]
        [SerializeField] private float depth = 1.1f;
        [SerializeField] private float minDepth = 0.4f;
        [SerializeField] private float maxDepth = 3f;
        [SerializeField] private float scrollSpeed = 0.35f;
        [SerializeField] private bool aimAwayFromCamera = true;
        [SerializeField] private bool logSwingSpeed = false;

        private Camera _cam;
        private BatSwinger _bat;

        private void Awake()
        {
            _bat = GetComponent<BatSwinger>();
        }

        private void OnEnable()
        {
            // Stop BatSwinger's hand follow from overwriting the mouse position every frame
            if (_bat != null) _bat.ExternallyDriven = Active;
        }

        private void OnDisable()
        {
            if (_bat != null) _bat.ExternallyDriven = false;
        }

        private bool Active { get { return !editorOnly || Application.isEditor; } }

        private void Update()
        {
            if (!Active) return;

            if (_bat != null && !_bat.IsHeld)
            {
                _bat.ExternallyDriven = false;
                return;
            }

            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) _cam = Object.FindObjectOfType<Camera>();
                if (_cam == null) return;   // Rig camera is not up yet
            }

            if (_bat != null) _bat.ExternallyDriven = true;

            depth = Mathf.Clamp(depth + Input.GetAxis("Mouse ScrollWheel") * scrollSpeed,
                                minDepth, maxDepth);

            Vector3 screen = Input.mousePosition;
            screen.z = depth;
            Vector3 world = _cam.ScreenToWorldPoint(screen);

            transform.position = world;

            if (aimAwayFromCamera)
            {
                Vector3 outward = world - _cam.transform.position;
                if (outward.sqrMagnitude > 1e-5f)
                    transform.rotation = Quaternion.LookRotation(outward.normalized, Vector3.up);
            }

            if (logSwingSpeed && _bat != null)
            {
                Debug.Log("[MouseTestBat] swing " + _bat.SwingSpeed.ToString("0.00") + " m/s  " +
                          (_bat.IsSwinging ? "HIT CAPABLE" : "too slow"));
            }
        }
    }
}
