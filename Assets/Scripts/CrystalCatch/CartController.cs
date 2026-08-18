using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CartController : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private TrackPath track;
        [SerializeField] private Transform carry;

        [Header("Speed (data, m/s)")]
        [SerializeField] private float baseSpeed = 4f;

        [SerializeField] private float maxAcceleration = 0.6f;

        [Header("Orientation")]
        [SerializeField] private bool followPitch = true;
        [SerializeField] private float turnSmoothing = 6f;

        [SerializeField] private float trackLookahead = 250f;

        [Header("Session behaviour")]
        [SerializeField] private bool moveDuringIntro = true;
        [SerializeField] private bool brakeOnEnd = true;

        /// Current speed, m/s. The spawner needs this to place items at a fixed LEAD TIME ahead
        public float CurrentSpeed { get { return _speed; } }

        /// Arc length along the track. This is the spawner's coordinate system, not world Z
        public float Distance { get { return _distance; } }

        public TrackPath Track { get { return track; } }

        private float _speed;
        private float _distance;
        private float _speedScale = 1f;

        private void Start()
        {
            if (track != null && !track.IsGenerated) track.Generate();
            ApplyToTransform(true);
        }

        private void Update()
        {
            float target = TargetSpeed();

            // The single point where speed is allowed to change, everything funnels through the clamp
            _speed = Mathf.MoveTowards(_speed, target, maxAcceleration * Time.deltaTime);
            _distance += _speed * Time.deltaTime;

            if (track != null) track.EnsureLength(_distance + trackLookahead);

            ApplyToTransform(false);
        }

        private void ApplyToTransform(bool snap)
        {
            if (track == null) return;

            transform.position = track.PositionAt(_distance);

            Quaternion want = track.RotationAt(_distance, followPitch);
            transform.rotation = snap || turnSmoothing <= 0f
                ? want
                : Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-turnSmoothing * Time.deltaTime));

            if (carry != null)
            {
                carry.position = transform.position;
                carry.rotation = transform.rotation;
            }
        }

        private float TargetSpeed()
        {
            if (game == null) return RoundSpeed();

            switch (game.Current)
            {
                case CrystalCatchGame.State.Intro:
                    return moveDuringIntro ? RoundSpeed() : 0f;

                case CrystalCatchGame.State.Ended:
                    return brakeOnEnd ? 0f : _speed;

                // Playing AND the between round tally, the cart never stops rolling
                default:
                    return RoundSpeed();
            }
        }

        /// Flat for the whole round. Clamped to what the track was actually generated for, so a
        /// round scale tuned too high can never outrun the curves' comfort budget
        private float RoundSpeed()
        {
            float speed = baseSpeed * _speedScale;
            if (track != null && track.TopSpeed > 0.01f) speed = Mathf.Min(speed, track.TopSpeed);
            return speed;
        }

        /// Multiplier on the curve's speed, for anything that touches pace. Safe to call with a step
        /// change, the acceleration clamp in Update ramps it regardless
        public void SetSpeedScale(float scale)
        {
            _speedScale = Mathf.Max(0f, scale);
        }
    }
}
