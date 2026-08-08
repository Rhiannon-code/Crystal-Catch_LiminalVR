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

        private readonly List<Queue<FxInstance>> _pools = new List<Queue<FxInstance>>();
        private Queue<FxInstance> _specialPool;

        private class FxInstance
        {
            public GameObject Go;
            public ParticleSystem Ps;
            public AudioSource Audio;
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

                ps.gameObject.SetActive(false);
                q.Enqueue(new FxInstance { Go = ps.gameObject, Ps = ps, Audio = src });
            }
            return q;
        }

        /// Play the burst for a caught crystal. pitch carries the value cue (rarer = brighter)
        public void PlayCrystal(CrystalColour colour, Vector3 position, float pitch)
        {
            int i = (int)colour;
            if (i < 0 || i >= _pools.Count) return;
            Play(_pools[i], position, pitch);
        }

        /// Play the burst for a collected power up/hazard
        public void PlaySpecial(Vector3 position)
        {
            Play(_specialPool, position, 1f);
        }

        private void Play(Queue<FxInstance> pool, Vector3 position, float pitch)
        {
            if (pool == null || pool.Count == 0) return;   // Exhausted, skip rather than allocate

            var fx = pool.Dequeue();
            fx.Go.transform.position = position;
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

        private IEnumerator ReturnAfter(Queue<FxInstance> pool, FxInstance fx)
        {
            yield return new WaitForSeconds(lifetime);
            fx.Ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            fx.Go.SetActive(false);
            pool.Enqueue(fx);
        }
    }
}
