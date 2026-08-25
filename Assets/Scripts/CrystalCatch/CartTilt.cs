using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CartTilt : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CartController cart;
        [SerializeField] private Transform cartBody;
        [SerializeField] private Transform rig;
        [SerializeField] private Transform headOverride;

        [Header("Deliberateness (data, metres and seconds)")]
        [SerializeField] private float engageLean = 0.22f;
        [SerializeField] private float fullLean = 0.42f;
        [SerializeField] private float engageHold = 0.18f;

        [Header("Neutral tracking")]
        [SerializeField] private float neutralAdaptSeconds = 2.5f;

        [Header("Tilt (data)")]
        [SerializeField] private float maxTiltDegrees = 12f;
        [SerializeField] private float engageSmoothing = 6f;
        [SerializeField] private float releaseSmoothing = 10f;

        [Header("Comfort")]
        [SerializeField, Range(0f, 1f)] private float viewRollShare = 0.3f;
        [SerializeField] private bool enableTilt = true;

        public float TiltDegrees { get { return _tilt; } }
        public bool IsTipping { get { return _heldFor >= engageHold; } }
        private Transform _head;
        private float _tilt;
        private float _neutralX;
        private bool _neutralReady;
        private float _heldFor;

        private void LateUpdate()
        {
            if (cart == null) return;

            float target = enableTilt ? TargetTilt() : 0f;
            float rate = Mathf.Abs(target) > Mathf.Abs(_tilt) ? engageSmoothing : releaseSmoothing;
            _tilt = Mathf.Lerp(_tilt, target, 1f - Mathf.Exp(-rate * Time.deltaTime));

            if (cartBody != null)
                cartBody.localRotation = Quaternion.Euler(0f, 0f, _tilt);

            if (rig != null && viewRollShare > 0.001f)
            {
                rig.rotation = Quaternion.AngleAxis(_tilt * viewRollShare, cart.transform.forward)
                             * rig.rotation;
            }
        }

        private float TargetTilt()
        {
            if (!ResolveHead()) return 0f;

            // Sideways offset in the CART's frame, so a climbing or turning track is not mistaken
            // for a lean
            float x = cart.transform.InverseTransformPoint(_head.position).x;

            if (!_neutralReady)
            {
                _neutralX = x;
                _neutralReady = true;
            }

            float lean = x - _neutralX;
            float magnitude = Mathf.Abs(lean);

            if (magnitude < engageLean)
            {
                _neutralX = Mathf.Lerp(_neutralX, x, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.05f, neutralAdaptSeconds)));
                _heldFor = 0f;
                return 0f;
            }

            _heldFor += Time.deltaTime;
            if (_heldFor < engageHold) return 0f;

            float t = Mathf.Clamp01((magnitude - engageLean) / Mathf.Max(0.01f, fullLean - engageLean));

            // Lean LEFT (negative offset) drops the left side, which is a positive roll about forward
            return -Mathf.Sign(lean) * t * maxTiltDegrees;
        }

        [ContextMenu("Recentre")]
        public void Recentre()
        {
            _neutralReady = false;
            _heldFor = 0f;
        }

        private bool ResolveHead()
        {
            if (_head != null) return true;

            if (headOverride != null) { _head = headOverride; return true; }

            // Don't use ?. on VRAvatar, it's a UnityEngine.Object, so null propagation bypasses
            // Unity's overloaded == and would sail past a destroyed avatar
            var avatar = VRAvatar.Active;
            if (avatar != null)
            {
                var limb = avatar.GetLimb(VRAvatarLimbType.Head);
                if (limb != null) { _head = limb.Transform; return true; }
            }

            var cam = Camera.main;
            if (cam == null) cam = Object.FindObjectOfType<Camera>();
            if (cam == null) return false;

            _head = cam.transform;
            return true;
        }
    }
}
