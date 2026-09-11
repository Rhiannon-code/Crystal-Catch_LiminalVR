using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class PowerUpFeedback : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private PowerUps powerUps;
        [SerializeField] private ComboTracker combo;
        [SerializeField] private SlowMotion slowMotion;
        [SerializeField] private Pistol[] guns;
        [SerializeField] private Transform floorPoint;

        [Header("Audio")]
        [SerializeField] private AudioClip[] milestoneClips;
        [SerializeField] private AudioClip[] fullAutoClips;
        [SerializeField] private AudioClip[] scattershotClips;
        [SerializeField] private AudioClip[] dualWieldClips;
        [SerializeField] private AudioClip[] slowMotionClips;
        [SerializeField] private AudioClip slowEnterClip;
        [SerializeField] private AudioClip slowExitClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

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

            if (combo != null) combo.MilestoneReached += OnMilestone;
            if (slowMotion != null) slowMotion.Changed += OnSlowMotion;
        }

        private void OnDisable()
        {
            if (powerUps != null)
            {
                powerUps.Granted -= OnGranted;
                powerUps.Expired -= OnExpired;
            }

            if (combo != null) combo.MilestoneReached -= OnMilestone;
            if (slowMotion != null) slowMotion.Changed -= OnSlowMotion;
        }

        private void OnMilestone(int chain)
        {
            Play(Pick(milestoneClips));
        }

        private void OnGranted(PowerUpKind kind, float seconds)
        {
            Play(Pick(ClipsFor(kind)));

            for (int i = 0; i < _bursts.Length; i++)
            {
                if (_bursts[i] != null && _bursts[i].gameObject.activeInHierarchy) _bursts[i].Play(true);
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

            for (int i = 0; i < _auras.Length; i++)
            {
                var aura = _auras[i];
                if (aura == null) continue;

                if (buffed && !aura.isPlaying) aura.Play(true);
                else if (!buffed && aura.isPlaying) aura.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private AudioClip[] ClipsFor(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.FullAuto: return fullAutoClips;
                case PowerUpKind.Scattershot: return scattershotClips;
                case PowerUpKind.DualWield: return dualWieldClips;
                default: return slowMotionClips;
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
