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
        [SerializeField] private bool startHeld = true;
        [SerializeField] private Vector3 fallbackLocalPosition = new Vector3(0.3f, 1.15f, 0.25f);
        [SerializeField] private Vector3 fallbackLocalEuler = new Vector3(-25f, 0f, 0f);
        [SerializeField] private Transform batVisual;
        [SerializeField] private CapsuleCollider hitVolume;

        [Header("Bat shape (data, metres)")]
        [SerializeField] private float baseLength = 0.65f;
        [SerializeField] private float baseRadius = 0.05f;
        [SerializeField] private float assistMargin = 0.04f;

        [Header("Model visual (data, model units along the shaft)")]
        [SerializeField] private bool visualIsModel;
        [SerializeField] private float modelGripAlongShaft = -0.50f;
        [SerializeField] private float modelHeadAlongShaft = 0.214f;

        [Header("Grip (data, metres)")]
        [Tooltip("Pushes the WHOLE pickaxe out along its own shaft, away from your hand. This is " +
                 "the dial for 'it feels cramped against my body'. baseLength only extends the far " +
                 "end - the grip stays pinned to your palm no matter how long you make it.")]
        [SerializeField] private float gripOffset = 0.12f;

        [Header("Swing gate (data)")]
        [SerializeField] private float minSwingSpeed = 1.6f;
        [SerializeField] private float speedSmoothing = 12f;
        [SerializeField] private Transform motionReference;

        [Header("Swing weight (data)")]
        [SerializeField] private bool swingWeight = true;
        [SerializeField] private float swingStiffness = 90f;
        [SerializeField] private float swingDamping = 13f;
        [SerializeField] private float maxLagDegrees = 35f;

        [Header("Swing audio (data)")]
        [SerializeField] private AudioClip[] swingClips;
        [SerializeField] private float swingSoundSpeed = 1.2f;
        [SerializeField, Range(0.1f, 0.9f)] private float swingRearmFraction = 0.5f;

        [SerializeField] private float swingVolumeMin = 0.35f;
        [SerializeField] private float swingVolumeMax = 1f;
        [SerializeField] private float swingPitchMin = 0.92f;
        [SerializeField] private float swingPitchMax = 1.15f;

        [Header("Bomb feedback")]
        [SerializeField] private Renderer[] ghostRenderers;
        [SerializeField] private Material normalMaterial;
        [SerializeField] private Material ghostedMaterial;

        public float SwingSpeed { get { return _swingSpeed; } }
        public bool IsSwinging { get { return _swingSpeed >= minSwingSpeed; } }
        public bool IsHeld { get; private set; }
        public VRAvatarLimbType Hand { get { return hand; } }
        public Vector3 GripPosition { get { return transform.position; } }
        private Transform _hand;
        private Vector3 _lastHeadPos;
        private Vector3 _headVelocity;
        private float _swingSpeed;
        private bool _hasLastPos;
        private bool _wasGhosted;

        // Last multipliers the shape was built for. NaN so the first ApplyShape always runs
        private float _shapedReach = float.NaN;
        private float _shapedArc = float.NaN;

        // Swing weight state. Angular velocity is in motionReference space, radians per second
        private Vector3 _swingAngularVelocity;
        private bool _swingPoseReady;

        private AudioSource _swingSource;
        private bool _swingArmed = true;

        private void Awake()
        {
            IsHeld = startHeld;
            ApplyShape();

            // 2D on purpose. The bat is ~40 cm from your face, so panning it buys nothing and a
            // hard panned swoosh on your own arm sounds wrong. It is also cheaper
            _swingSource = gameObject.AddComponent<AudioSource>();
            _swingSource.playOnAwake = false;
            _swingSource.spatialBlend = 0f;
        }

        /// Set by a desktop test driver (MouseTestBat) so hand following does not fight it
        public bool ExternallyDriven { get; set; }

        public void SetHeld(bool held)
        {
            IsHeld = held;
            _hand = null;
            _hasLastPos = false;
            _swingSpeed = 0f;
            _headVelocity = Vector3.zero;
            ResetSwingPose();
        }

        public void AssignHand(VRAvatarLimbType limbType)
        {
            hand = limbType;
            _hand = null;
            ResetSwingPose();
        }

        /// The holster to hand teleport is not a swing. Without this the spring is handed a 180
        /// degree error in one frame and the pickaxe cartwheels, exactly as the speed sampler had
        /// to be reset for the same reason
        private void ResetSwingPose()
        {
            _swingPoseReady = false;
            _swingAngularVelocity = Vector3.zero;
        }

        private void Update()
        {
            if (!IsHeld) HoldFallbackPose();
            else if (followHandTransform && !ExternallyDriven) FollowHand();

            ApplyShape();
            ApplyGhosting();
        }

        private void LateUpdate()
        {
            Vector3 head = HeadPosition();
            Vector3 sample = motionReference != null ? motionReference.InverseTransformPoint(head) : head;

            if (_hasLastPos && Time.deltaTime > 0f)
            {
                Vector3 localVelocity = (sample - _lastHeadPos) / Time.deltaTime;

                _headVelocity = motionReference != null
                    ? motionReference.TransformVector(localVelocity)
                    : localVelocity;

                float t = 1f - Mathf.Exp(-speedSmoothing * Time.deltaTime);
                _swingSpeed = Mathf.Lerp(_swingSpeed, localVelocity.magnitude, t);
            }

            UpdateSwingSound();

            _lastHeadPos = sample;
            _hasLastPos = true;
        }

        private void UpdateSwingSound()
        {
            if (_swingSource == null || swingClips == null || swingClips.Length == 0) return;
            if (!IsHeld) { _swingArmed = true; return; }

            if (_swingSpeed <= swingSoundSpeed * swingRearmFraction) _swingArmed = true;

            if (!_swingArmed || _swingSpeed < swingSoundSpeed) return;
            _swingArmed = false;

            var clip = swingClips[Random.Range(0, swingClips.Length)];
            if (clip == null) return;

            // Same shape as PulseOnHit's haptic scaling, so the swoosh and the rumble agree on
            // what counts as a hard swing
            float t = Mathf.InverseLerp(swingSoundSpeed, swingSoundSpeed * 3f, _swingSpeed);
            _swingSource.volume = Mathf.Lerp(swingVolumeMin, swingVolumeMax, t);
            _swingSource.pitch = Mathf.Lerp(swingPitchMin, swingPitchMax, t);
            _swingSource.PlayOneShot(clip, _swingSource.volume);
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

                // Resolving the limb is a teleport, not a swing
                ResetSwingPose();
            }

            if (!swingWeight)
            {
                transform.SetPositionAndRotation(GripPoint(_hand.rotation), _hand.rotation);
                return;
            }

            // Rotation first: the offset runs along the bat's OWN shaft, so with swing weight on
            // the grip point sweeps its arc with the lagged head instead of fighting it
            Quaternion rotation = WeightedRotation(_hand.rotation);
            transform.rotation = rotation;
            transform.position = GripPoint(rotation);
        }

        /// Where the bat's origin sits. Offset along its own forward, so the pickaxe reads as being
        /// held further down an invisible haft rather than clutched against your chest
        private Vector3 GripPoint(Quaternion rotation)
        {
            if (_hand == null) return transform.position;
            return Mathf.Abs(gripOffset) < 0.0001f
                ? _hand.position
                : _hand.position + rotation * Vector3.forward * gripOffset;
        }

        private Quaternion WeightedRotation(Quaternion handWorld)
        {
            Quaternion frame = motionReference != null ? motionReference.rotation : Quaternion.identity;
            Quaternion frameInverse = Quaternion.Inverse(frame);

            Quaternion target = frameInverse * handWorld;

            if (!_swingPoseReady)
            {
                _swingPoseReady = true;
                _swingAngularVelocity = Vector3.zero;
                return frame * target;
            }

            Quaternion current = frameInverse * transform.rotation;

            // A frame hitch must not detonate an explicitly integrated spring
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            Quaternion delta = target * Quaternion.Inverse(current);
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

            float angle;
            Vector3 axis;
            delta.ToAngleAxis(out angle, out axis);
            if (angle > 180f) angle -= 360f;

            bool axisUsable = axis.sqrMagnitude > 1e-8f
                              && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x)
                              && !float.IsNaN(angle) && !float.IsInfinity(angle);

            if (axisUsable)
            {
                Vector3 restoring = axis.normalized * (angle * Mathf.Deg2Rad) * swingStiffness;
                _swingAngularVelocity += (restoring - _swingAngularVelocity * swingDamping) * dt;
            }

            float speed = _swingAngularVelocity.magnitude;
            if (speed > 1e-5f)
            {
                current = Quaternion.AngleAxis(speed * dt * Mathf.Rad2Deg,
                                               _swingAngularVelocity / speed) * current;
            }

            // The head may trail, but it must never fold back through the handle
            float lag = Quaternion.Angle(current, target);
            if (lag > maxLagDegrees)
            {
                current = Quaternion.RotateTowards(current, target, lag - maxLagDegrees);
                _swingAngularVelocity *= 0.5f;
            }

            return frame * current;
        }

        private void HoldFallbackPose()
        {
            if (transform.parent == null) return;
            transform.localPosition = fallbackLocalPosition;
            transform.localRotation = Quaternion.Euler(fallbackLocalEuler);
        }

        private void ApplyShape()
        {
            float reach = game != null ? game.ReachMultiplier : 1f;
            float arc = game != null ? game.ArcMultiplier : 1f;

            // Writing a CapsuleCollider's geometry makes PhysX rebuild the shape, so only touch it
            // when a power up has actually resized the bat, not on every frame of every swing
            if (reach == _shapedReach && arc == _shapedArc) return;
            _shapedReach = reach;
            _shapedArc = arc;

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
                hitVolume.direction = 2;                    // Z aligned capsule
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

            if (!IsHeld) return;

            if (!game.CollectionEnabled) return;

            if (_swingSpeed < minSwingSpeed) return;

            var crystal = other.GetComponent<Crystal>();
            if (crystal != null)
            {
                crystal.Hit(game, _headVelocity);
                PulseOnHit();
                return;
            }

            var special = other.GetComponent<SpecialItem>();
            if (special != null)
            {
                special.Hit(game, _headVelocity);
                PulseOnHit();
            }
        }

        private static bool _warnedNoHaptics;

        private void PulseOnHit()
        {
            if (HapticPulse.Instance == null)
            {
                // This exact silent return is what hid the haptics being absent entirely: the
                // component was never in the scene, so every hit skipped straight past it
                if (!_warnedNoHaptics)
                {
                    _warnedNoHaptics = true;
                    Debug.LogWarning("[BatSwinger] Hit registered but there is no HapticPulse in " +
                                     "the scene, so no vibration will ever fire. Add one.");
                }
                return;
            }

            float strength = Mathf.InverseLerp(minSwingSpeed, minSwingSpeed * 3f, _swingSpeed);
            HapticPulse.Instance.Hit(hand, strength);
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
