using UnityEngine;
using Liminal.SDK.VR.Avatars;
using IntuitiveDesigns.CrystalCatch;

namespace IntuitiveDesigns.ShootingRange
{
    public class PlayerHitFeedback : MonoBehaviour
    {
        [SerializeField] private RangeGame game;

        [Header("Audio")]
        [SerializeField] private AudioClip[] hitClips;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        [Header("Haptics (data)")]
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 1f;
        [SerializeField] private float hapticSeconds = 0.25f;

        private AudioSource _audio;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            if (game != null) game.PlayerHit += OnPlayerHit;
        }

        private void OnDisable()
        {
            if (game != null) game.PlayerHit -= OnPlayerHit;
        }

        private void OnPlayerHit(Vector3 from)
        {
            if (hitClips != null && hitClips.Length > 0)
                _audio.PlayOneShot(hitClips[Random.Range(0, hitClips.Length)], volume);

            var haptics = HapticPulse.Instance;
            if (haptics == null) return;

            haptics.Pulse(VRAvatarLimbType.LeftHand, hapticAmplitude, hapticSeconds);
            haptics.Pulse(VRAvatarLimbType.RightHand, hapticAmplitude, hapticSeconds);
        }
    }
}
