using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class ImpactFX : MonoBehaviour
    {
        public static ImpactFX Instance { get; private set; }

        [Header("Impact burst")]
        [SerializeField] private ParticleSystem impactPrefab;
        [SerializeField] private int burstCopies = 8;
        [SerializeField] private float burstLifetime = 1f;

        [Header("Impact audio")]
        [SerializeField] private AudioClip[] impactClips;
        [SerializeField, Range(0f, 1f)] private float impactVolume = 0.55f;

        [Header("Shared voices")]
        [SerializeField] private int voices = 12;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.08f;
        [SerializeField] private float maxAudioDistance = 25f;

        // Unity never pitches audio with timeScale, so the world's slow motion has to be heard on purpose
        [SerializeField, Range(0.1f, 1f)] private float minSlowPitch = 0.5f;

        private readonly Queue<ParticleSystem> _bursts = new Queue<ParticleSystem>();
        private AudioSource[] _voices;
        private int _nextVoice;

        private void Awake()
        {
            Instance = this;

            if (impactPrefab != null)
            {
                for (int i = 0; i < burstCopies; i++)
                {
                    var ps = Instantiate(impactPrefab, transform);
                    var main = ps.main;
                    main.loop = false;
                    main.playOnAwake = false;
                    ps.gameObject.SetActive(false);
                    _bursts.Enqueue(ps);
                }
            }

            _voices = new AudioSource[Mathf.Max(1, voices)];
            for (int i = 0; i < _voices.Length; i++)
            {
                // Each voice needs its own transform. Sharing this object's would move the pooled
                // particles parented to it every time a crate clattered somewhere else
                var holder = new GameObject("Voice " + i);
                holder.transform.SetParent(transform, false);

                var src = holder.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.maxDistance = maxAudioDistance;
                _voices[i] = src;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void PlayImpact(Vector3 point, Vector3 normal)
        {
            PlayBurst(point, normal);
            PlayClip(Pick(impactClips), point, impactVolume);
        }

        public void PlayClip(AudioClip clip, Vector3 point, float volume)
        {
            if (clip == null || _voices == null) return;

            var src = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;

            src.transform.position = point;
            src.pitch = (1f + (pitchJitter > 0f ? Random.Range(-pitchJitter, pitchJitter) : 0f)) *
                        Mathf.Max(minSlowPitch, Time.timeScale);
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void PlayBurst(Vector3 point, Vector3 normal)
        {
            if (_bursts.Count == 0) return;

            var ps = _bursts.Dequeue();
            ps.transform.position = point;
            ps.transform.rotation = normal.sqrMagnitude > 1e-6f
                                  ? Quaternion.LookRotation(normal)
                                  : Quaternion.identity;
            ps.gameObject.SetActive(true);
            ps.Play(true);
            StartCoroutine(ReturnBurst(ps));
        }

        private IEnumerator ReturnBurst(ParticleSystem ps)
        {
            yield return new WaitForSeconds(burstLifetime);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            _bursts.Enqueue(ps);
        }

        private static AudioClip Pick(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[Random.Range(0, clips.Length)];
        }
    }
}
