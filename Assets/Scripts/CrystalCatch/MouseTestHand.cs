using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    [RequireComponent(typeof(HandCollector))]
    public class MouseTestHand : MonoBehaviour
    {
        [SerializeField] private bool editorOnly = true;

        [Header("Depth (scroll wheel)")]
        [SerializeField] private float depth = 0.6f;
        [SerializeField] private float minDepth = 0.25f;
        [SerializeField] private float maxDepth = 2f;
        [SerializeField] private float scrollSpeed = 0.35f;
        [SerializeField] private bool drawGizmo = true;

        private Camera _cam;

        private void LateUpdate()
        {
            if (editorOnly && !Application.isEditor) return;

            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;   // VR rig camera isn't up yet
            }

            depth = Mathf.Clamp(depth + Input.GetAxis("Mouse ScrollWheel") * scrollSpeed, minDepth, maxDepth);

            Vector3 screen = Input.mousePosition;
            screen.z = depth;
            transform.position = _cam.ScreenToWorldPoint(screen);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmo) return;
            var col = GetComponent<SphereCollider>();
            if (col == null) return;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, col.radius);
        }
    }
}
