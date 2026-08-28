using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CaveAmbience : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CartController cart;

        [Header("Bed: always present")]
        [SerializeField] private AudioClip bedClip;
        [SerializeField, Range(0f, 1f)] private float bedVolume = 0.45f;

        [Header("Cart rumble: rises with speed")]
        [SerializeField] private AudioClip rumbleClip;
        [SerializeField, Range(0f, 1f)] private float rumbleVolumeAtRest = 0.05f;
        [SerializeField, Range(0f, 1f)] private float rumbleVolumeAtSpeed = 0.6f;
        [SerializeField] private float rumblePitchAtRest = 0.8f;
        [SerializeField] private float rumblePitchAtSpeed = 1.15f;

        [Header("Wind: only at pace")]
        [SerializeField] private AudioClip windClip;
        [SerializeField, Range(0f, 1f)] private float windVolumeAtSpeed = 0.35f;
        [SerializeField] private float speedForFullIntensity = 8f;

        [Header("Feel")]
        [SerializeField] private float fadeInSeconds = 2f;
        [SerializeField] private float responseSmoothing = 2.5f;

        private AudioSource _bed;
        private AudioSource _rumble;
        private AudioSource _wind;
        private float _intensity;
        private float _fade;

        private void Awake()
        {
            _bed = Make(bedClip);
            _rumble = Make(rumbleClip);
            _wind = Make(windClip);
        }

        private AudioSource Make(AudioClip clip)
        {
            if (clip == null) return null;

            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 0f;

            // Offset so two layers sharing a clip do not phase against each other
            src.time = Random.Range(0f, Mathf.Max(0.01f, clip.length * 0.5f));
            src.Play();
            return src;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (fadeInSeconds > 0.01f) _fade = Mathf.MoveTowards(_fade, 1f, dt / fadeInSeconds);
            else _fade = 1f;

            float speed = cart != null ? cart.CurrentSpeed : 0f;
            float target = speedForFullIntensity > 0.01f
                ? Mathf.Clamp01(speed / speedForFullIntensity)
                : 0f;

            // Smoothed, or a speed change on a round boundary steps the whole mix audibly
            _intensity = Mathf.Lerp(_intensity, target, 1f - Mathf.Exp(-responseSmoothing * dt));

            if (_bed != null) _bed.volume = bedVolume * _fade;

            if (_rumble != null)
            {
                _rumble.volume = Mathf.Lerp(rumbleVolumeAtRest, rumbleVolumeAtSpeed, _intensity) * _fade;
                _rumble.pitch = Mathf.Lerp(rumblePitchAtRest, rumblePitchAtSpeed, _intensity);
            }

            // Wind is squared so it stays out of the way at a walking pace and only arrives when
            // the cart is genuinely moving, rather than sitting under everything the whole run
            if (_wind != null)
                _wind.volume = windVolumeAtSpeed * _intensity * _intensity * _fade;
        }
    }
}
