using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class BatSwinger : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private VRAvatarLimbType hand = VRAvatarLimbType.RightHand;
        [SerializeField] private bool followHandTransform = true;
        [SerializeField] private Vector3 fallbackLocalPosition = new Vector3(0.3f, 1.15f, 0.25f);
        [SerializeField] private Vector3 fallbackLocalEuler = new Vector3(-25f, 0f, 0f);
        [SerializeField] private Transform batVisual;
        [SerializeField] private CapsuleCollider hitVolume;

        [Header("Bat shape (data, metres)")]
        [SerializeField] private float baseLength = 0.65f;
        [SerializeField] private float baseRadius = 0.05f;
        [SerializeField] private float assistMargin = 0.04f;

        [Header("Model visual (data, model units along the shaft)")]
        // The primitive capsule can be squashed to any length and radius. A real mesh cannot, so
        // when batVisual is a model it gets scaled UNIFORMLY instead, using these two measurements
        [SerializeField] private bool visualIsModel;
        [SerializeField] private float modelGripAlongShaft = -0.50f;
        [SerializeField] private float modelHeadAlongShaft = 0.214f;

        [Header("Swing gate (data)")]
        [SerializeField] private float minSwingSpeed = 1.6f;
        [SerializeField] private float speedSmoothing = 12f;
        [SerializeField] private Transform motionReference;

        [Header("Bomb feedback")]
        [SerializeField] private Renderer[] ghostRenderers;
        [SerializeField] private Material normalMaterial;
        [SerializeField] private Material ghostedMaterial;

        /// Current smoothed speed of the bat head, m/s. Exposed for HUD/debug and MouseTestHand
        public float SwingSpeed { get { return _swingSpeed; } }

        /// True when the bat is moving fast enough for contact to count.
        public bool IsSwinging { get { return _swingSpeed >= minSwingSpeed; } }

        private Transform _hand;
        private Vector3 _lastHeadPos;
        private Vector3 _headVelocity;
        private float _swingSpeed;
        private bool _hasLastPos;
        private bool _wasGhosted;

        private void Awake()
        {
            ApplyShape();
        }

        /// Set by a desktop test driver (MouseTestBat) so hand following does not fight it
        public bool ExternallyDriven { get; set; }

        private void Update()
        {
            if (followHandTransform && !ExternallyDriven) FollowHand();
            ApplyShape();
            ApplyGhosting();
        }

        private void LateUpdate()
        {
            // Sampled in LateUpdate so it measures the bat's FINAL position for the frame, after the
            // hand follow above and after anything else that moves the rig
            Vector3 head = HeadPosition();
            Vector3 sample = motionReference != null ? motionReference.InverseTransformPoint(head) : head;

            if (_hasLastPos && Time.deltaTime > 0f)
            {
                Vector3 localVelocity = (sample - _lastHeadPos) / Time.deltaTime;

                // Gate on the cart relative magnitude, report direction in world space so hit
                // reactions still point the right way
                _headVelocity = motionReference != null
                    ? motionReference.TransformVector(localVelocity)
                    : localVelocity;

                float t = 1f - Mathf.Exp(-speedSmoothing * Time.deltaTime);
                _swingSpeed = Mathf.Lerp(_swingSpeed, localVelocity.magnitude, t);
            }

            _lastHeadPos = sample;
            _hasLastPos = true;
        }

        private void FollowHand()
        {
            if (_hand == null)
            {
                var avatar = VRAvatar.Active;
                if (avatar != null)
                {
                    var limb = avatar.GetLimb(hand);
                    if (limb != null) _hand = limb.Transform;
                }

                if (_hand == null)
                {
                    HoldFallbackPose();
                    return;
                }
            }

            transform.SetPositionAndRotation(_hand.position, _hand.rotation);
        }

        /// Where the bat sits when there is no tracked hand. Local to the parent, which the builder
        /// makes the cart, so the bat rides along and stays visible even with no controller
        private void HoldFallbackPose()
        {
            if (transform.parent == null) return;
            transform.localPosition = fallbackLocalPosition;
            transform.localRotation = Quaternion.Euler(fallbackLocalEuler);
        }

        /// The bat runs along local +Z from the grip. Reach scales its length, Arc scales its radius,
        /// and both come from the session manager so power up timers stay in one place
        private void ApplyShape()
        {
            float reach = game != null ? game.ReachMultiplier : 1f;
            float arc = game != null ? game.ArcMultiplier : 1f;

            float length = baseLength * reach;
            float radius = baseRadius * arc;

            if (batVisual != null)
            {
                if (visualIsModel) ShapeModelVisual(length);
                else
                {
                    batVisual.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
                    batVisual.localPosition = new Vector3(0f, 0f, length * 0.5f);
                }
            }

            if (hitVolume != null)
            {
                hitVolume.direction = 2;                    // Z-aligned capsule
                hitVolume.height = length + assistMargin * 2f;
                hitVolume.radius = radius + assistMargin;
                hitVolume.center = new Vector3(0f, 0f, length * 0.5f);
                hitVolume.isTrigger = true;
            }
        }

        private void ShapeModelVisual(float length)
        {
            float span = modelHeadAlongShaft - modelGripAlongShaft;
            if (span <= 0.0001f) return;

            float scale = length / span;
            batVisual.localScale = new Vector3(scale, scale, scale);
            batVisual.localPosition = new Vector3(0f, 0f, -modelGripAlongShaft * scale);
        }

        private Vector3 HeadPosition()
        {
            float reach = game != null ? game.ReachMultiplier : 1f;
            return transform.position + transform.forward * (baseLength * reach);
        }

        private void ApplyGhosting()
        {
            bool ghosted = game != null && !game.CollectionEnabled;
            if (ghosted == _wasGhosted) return;
            _wasGhosted = ghosted;

            var mat = ghosted ? ghostedMaterial : normalMaterial;
            if (mat == null || ghostRenderers == null) return;

            for (int i = 0; i < ghostRenderers.Length; i++)
                if (ghostRenderers[i] != null) ghostRenderers[i].sharedMaterial = mat;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (game == null) return;

            // Bomb hazard, swings pass straight through
            if (!game.CollectionEnabled) return;

            // The swing gate. Contact without speed is not a hit
            if (_swingSpeed < minSwingSpeed) return;

            var crystal = other.GetComponent<Crystal>();
            if (crystal != null)
            {
                crystal.Hit(game, _headVelocity);
                return;
            }

            var special = other.GetComponent<SpecialItem>();
            if (special != null) special.Hit(game, _headVelocity);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            float reach = game != null ? game.ReachMultiplier : 1f;
            Gizmos.color = IsSwinging ? Color.green : Color.grey;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * (baseLength * reach));
        }
#endif
    }
}
