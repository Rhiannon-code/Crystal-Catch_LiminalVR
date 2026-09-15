using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class PowerUpFeedback : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PowerUps powerUps;
        [SerializeField] private SlowMotion slowMotion;
        [SerializeField] private Pistol[] guns;
        [SerializeField] private Transform floorPoint;

        [Header("Audio")]
        [SerializeField] private AudioClip[] fullAutoClips;
        [SerializeField] private AudioClip[] scattershotClips;
        [SerializeField] private AudioClip[] slowMotionClips;
        [SerializeField] private AudioClip slowEnterClip;
        [SerializeField] private AudioClip slowExitClip;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        [Header("VFX")]
        [SerializeField] private ParticleSystem grantBurstPrefab;
        [SerializeField] private ParticleSystem gunAuraPrefab;
        [SerializeField] private ParticleSystem slowCirclePrefab;

        private AudioSource _audio;
        private ParticleSystem[] _auras;
        private ParticleSystem[] _bursts;
        private ParticleSystem _circle;

        private void Awake()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;

            int count = guns != null ? guns.Length : 0;
            _auras = new ParticleSystem[count];
            _bursts = new ParticleSystem[count];

            for (int i = 0; i < count; i++)
            {
                if (guns[i] == null) continue;

                _auras[i] = Spawn(gunAuraPrefab, guns[i].Muzzle, false);
                _bursts[i] = Spawn(grantBurstPrefab, guns[i].Muzzle, true);
            }

            if (slowCirclePrefab != null && floorPoint != null)
                _circle = Spawn(slowCirclePrefab, floorPoint, true);
        }

        private void OnEnable()
        {
            if (powerUps != null)
            {
                powerUps.Granted += OnGranted;
                powerUps.Expired += OnExpired;
            }

            if (slowMotion != null) slowMotion.Changed += OnSlowMotion;
        }

        private void OnDisable()
        {
            if (powerUps != null)
            {
                powerUps.Granted -= OnGranted;
                powerUps.Expired -= OnExpired;
            }

            if (slowMotion != null) slowMotion.Changed -= OnSlowMotion;
        }

        private void OnGranted(PowerUpKind kind, float seconds)
        {
            Play(Pick(ClipsFor(kind)));

            Color colour = powerUps.Colour(kind);
            for (int i = 0; i < _bursts.Length; i++)
            {
                var burst = _bursts[i];
                if (burst == null || !burst.gameObject.activeInHierarchy) continue;

                Tint(burst, colour);
                burst.Play(true);
            }

            RefreshAuras();
        }

        private void OnExpired(PowerUpKind kind)
        {
            RefreshAuras();
        }

        private void OnSlowMotion(bool active)
        {
            Play(active ? slowEnterClip : slowExitClip);
            if (_circle == null) return;

            if (active) _circle.Play(true);
            else _circle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void RefreshAuras()
        {
            bool buffed = powerUps != null &&
                          (powerUps.IsActive(PowerUpKind.FullAuto) || powerUps.IsActive(PowerUpKind.Scattershot));

            PowerUpKind latest = PowerUpKind.FullAuto;
            if (buffed) powerUps.TryLatest(out latest);

            for (int i = 0; i < _auras.Length; i++)
            {
                var aura = _auras[i];
                if (aura == null) continue;

                if (buffed)
                {
                    Tint(aura, powerUps.Colour(latest));
                    if (!aura.isPlaying) aura.Play(true);
                }
                else if (aura.isPlaying)
                {
                    aura.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        private AudioClip[] ClipsFor(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.FullAuto: return fullAutoClips;
                case PowerUpKind.Scattershot: return scattershotClips;
                default: return slowMotionClips;
            }
        }

        private static void Tint(ParticleSystem root, Color colour)
        {
            foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.startColor = colour;
            }
        }

        private static ParticleSystem Spawn(ParticleSystem prefab, Transform parent, bool unscaled)
        {
            if (prefab == null || parent == null) return null;

            var ps = Instantiate(prefab, parent);
            ps.transform.localPosition = Vector3.zero;
            ps.transform.localRotation = Quaternion.identity;

            // Feedback for the player's own action reads as sluggish if it slows down with the world
            foreach (var system in ps.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.playOnAwake = false;
                main.useUnscaledTime = unscaled;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private void Play(AudioClip clip)
        {
            if (clip != null && _audio != null) _audio.PlayOneShot(clip, volume);
        }

        private static AudioClip Pick(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[Random.Range(0, clips.Length)];
        }
    }
}
