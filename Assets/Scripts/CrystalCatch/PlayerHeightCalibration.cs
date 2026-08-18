using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class PlayerHeightCalibration : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CartController cart;
        [SerializeField] private Transform headOverride;

        [Header("Calibration (data)")]
        // What the obstacle prefabs were authored against
        [SerializeField] private float referenceEyeHeight = 1.6f;

        // Sampled across the 3-2-1 countdown, which is the one moment the player is reliably stood
        // still and looking forward, and is already dead time
        [SerializeField] private float sampleSeconds = 2.5f;

        // A bad sample must not be able to bury a beam in the floor or put it out of reach
        [SerializeField] private float maxOffset = 0.35f;

        /// Highest the head reached while standing, measured from the cart floor
        public float StandingEyeHeight { get; private set; }

        public bool IsCalibrated { get; private set; }

        /// Add this to anything authored against referenceEyeHeight
        public float HeightOffset
        {
            get
            {
                if (!IsCalibrated) return 0f;
                return Mathf.Clamp(StandingEyeHeight - referenceEyeHeight, -maxOffset, maxOffset);
            }
        }

        private Transform _head;
        private float _elapsed;

        [ContextMenu("Recalibrate")]
        public void Recalibrate()
        {
            StandingEyeHeight = 0f;
            IsCalibrated = false;
            _elapsed = 0f;
        }

        private void Update()
        {
            if (IsCalibrated) return;
            if (!ResolveHead()) return;

            // Relative to the cart, not to world zero. The track climbs and descends, so an absolute
            // height would read the gradient as the player growing
            float floorY = cart != null ? cart.transform.position.y : 0f;
            float height = _head.position.y - floorY;

            // MAX rather than average: the player may glance down at the cart during the count, and
            // looking down lowers the headset. The tallest sample is the one that means "standing"
            if (height > StandingEyeHeight) StandingEyeHeight = height;

            _elapsed += Time.deltaTime;
            if (_elapsed < sampleSeconds) return;

            // A sample this far out is a tracking glitch or a player who sat down, not a person
            if (StandingEyeHeight < 0.8f || StandingEyeHeight > 2.4f)
            {
                Debug.LogWarning("[PlayerHeightCalibration] Implausible eye height " +
                                 StandingEyeHeight.ToString("0.00") + " m, falling back to " +
                                 referenceEyeHeight + " m.");
                StandingEyeHeight = referenceEyeHeight;
            }

            IsCalibrated = true;
            Debug.Log("[PlayerHeightCalibration] Standing eye height " +
                      StandingEyeHeight.ToString("0.00") + " m, obstacles shift by " +
                      HeightOffset.ToString("+0.00;-0.00") + " m.");
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
