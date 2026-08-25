using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{

    public class CatchFXPool : MonoBehaviour
    {
        public static CatchFXPool Instance { get; private set; }

        [Header("Burst prefab per crystal colour (Blue, Green, Purple, Gold)")]
        [SerializeField] private ParticleSystem[] burstPrefabs = new ParticleSystem[4];
        [SerializeField] private ParticleSystem specialBurstPrefab;

        [Header("Pool")]
        [SerializeField] private int copiesPerEffect = 4;
        [SerializeField] private float lifetime = 1.2f;

        [Header("Audio (optional, greybox placeholder)")]
        [SerializeField] private AudioClip catchClip;
        [SerializeField] private float volume = 0.7f;

        [Header("Shatter direction")]
        [SerializeField] private float shardVelocityScale = 0.6f;
        [SerializeField] private float maxShardSpeed = 6f;
        [SerializeField] private float minDirectionalSpeed = 0.2f;

        private readonly List<Queue<FxInstance>> _pools = new List<Queue<FxInstance>>();
        private Queue<FxInstance> _specialPool;

        private class FxInstance
        {
            public GameObject Go;
            public ParticleSystem Ps;
            public AudioSource Audio;
            public ParticleSystem[] Children;
        }

        private void Awake()
        {
            Instance = this;

            for (int i = 0; i < burstPrefabs.Length; i++)
                _pools.Add(BuildPool(burstPrefabs[i]));

            _specialPool = BuildPool(specialBurstPrefab);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private Queue<FxInstance> BuildPool(ParticleSystem prefab)
        {
            var q = new Queue<FxInstance>();
            if (prefab == null) return q;

            for (int i = 0; i < copiesPerEffect; i++)
            {
                var ps = Instantiate(prefab, transform);

                // The pack's effects loop by default, a catch burst is a one shot
                var main = ps.main;
                main.loop = false;
                main.playOnAwake = false;

                foreach (var child in ps.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var m = child.main;
                    m.loop = false;
                    m.playOnAwake = false;
                }

                var src = ps.gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;   // Positional, so catches read from where they happened
                src.volume = volume;

                // Children only, the root is handled separately and must not be biased twice
                var nested = new List<ParticleSystem>(ps.GetComponentsInChildren<ParticleSystem>(true));
                nested.Remove(ps);

                ps.gameObject.SetActive(false);
                q.Enqueue(new FxInstance
                {
                    Go = ps.gameObject,
                    Ps = ps,
                    Audio = src,
                    Children = nested.ToArray()
                });
            }
            return q;
        }

        /// Play the burst for a caught crystal. pitch carries the value cue (rarer = brighter)
        public void PlayCrystal(CrystalColour colour, Vector3 position, float pitch)
        {
            PlayCrystal(colour, position, pitch, Vector3.zero);
        }

        public void PlayCrystal(CrystalColour colour, Vector3 position, float pitch, Vector3 hitVelocity)
        {
            int i = (int)colour;
            if (i < 0 || i >= _pools.Count) return;
            Play(_pools[i], position, pitch, hitVelocity);
        }

        /// Play the burst for a collected power up/hazard
        public void PlaySpecial(Vector3 position)
        {
            PlaySpecial(position, Vector3.zero);
        }

        public void PlaySpecial(Vector3 position, Vector3 hitVelocity)
        {
            Play(_specialPool, position, 1f, hitVelocity);
        }

        private void Play(Queue<FxInstance> pool, Vector3 position, float pitch, Vector3 hitVelocity)
        {
            if (pool == null || pool.Count == 0) return;   // Exhausted, skip rather than allocate

            var fx = pool.Dequeue();
            fx.Go.transform.position = position;
            AimShatter(fx, hitVelocity);
            fx.Go.SetActive(true);
            fx.Ps.Play(true);

            if (catchClip != null)
            {
                fx.Audio.clip = catchClip;
                fx.Audio.pitch = pitch;
                fx.Audio.Play();
            }

            StartCoroutine(ReturnAfter(pool, fx));
        }

        private void AimShatter(FxInstance fx, Vector3 hitVelocity)
        {
            float speed = hitVelocity.magnitude;
            bool directional = speed >= minDirectionalSpeed;

            fx.Go.transform.rotation = directional
                ? Quaternion.LookRotation(hitVelocity / speed, Vector3.up)
                : Quaternion.identity;

            Vector3 drift = directional
                ? Vector3.ClampMagnitude(hitVelocity * shardVelocityScale, maxShardSpeed)
                : Vector3.zero;

            ApplyDrift(fx.Ps, drift, directional);
            for (int i = 0; i < fx.Children.Length; i++)
                ApplyDrift(fx.Children[i], drift, directional);
        }

        private static void ApplyDrift(ParticleSystem ps, Vector3 drift, bool enable)
        {
            if (ps == null) return;

            var vel = ps.velocityOverLifetime;

            if (!enable) { vel.enabled = false; return; }

            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(drift.x);
            vel.y = new ParticleSystem.MinMaxCurve(drift.y);
            vel.z = new ParticleSystem.MinMaxCurve(drift.z);
        }

        private IEnumerator ReturnAfter(Queue<FxInstance> pool, FxInstance fx)
        {
            yield return new WaitForSeconds(lifetime);
            fx.Ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            fx.Go.SetActive(false);
            pool.Enqueue(fx);
        }
    }
}
