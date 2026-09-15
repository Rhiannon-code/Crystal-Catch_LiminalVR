using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class ShatterPool : MonoBehaviour
    {
        public static ShatterPool Instance { get; private set; }

        [SerializeField] private Rigidbody fragmentPrefab;
        [SerializeField] private int poolSize = 60;

        [Header("Burst (data)")]
        [SerializeField] private float lifetime = 2.5f;
        [SerializeField] private float scatter = 0.14f;
        [SerializeField] private float sidewaysShare = 0.6f;
        [SerializeField] private float spin = 4f;

        [Header("Pop effect")]
        [SerializeField] private ParticleSystem popPrefab;
        [SerializeField] private int popCopies = 6;
        [SerializeField] private float popLifetime = 1.2f;

        [Header("Audio")]
        [SerializeField] private AudioClip[] shatterClips;
        [SerializeField, Range(0f, 1f)] private float shatterVolume = 0.7f;

        private readonly Queue<Rigidbody> _idle = new Queue<Rigidbody>();
        private readonly Queue<ParticleSystem> _pops = new Queue<ParticleSystem>();

        private void Awake()
        {
            Instance = this;

            if (popPrefab != null)
            {
                for (int i = 0; i < popCopies; i++)
                {
                    var pop = Instantiate(popPrefab, transform);
                    pop.gameObject.SetActive(false);
                    _pops.Enqueue(pop);
                }
            }

            if (fragmentPrefab == null)
            {
                Debug.LogWarning("[ShatterPool] No fragment prefab assigned. Targets will vanish without debris.");
                return;
            }

            for (int i = 0; i < poolSize; i++)
            {
                var piece = Instantiate(fragmentPrefab, transform);
                piece.gameObject.SetActive(false);
                _idle.Enqueue(piece);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Burst(Vector3 at, int count, Vector3 direction, float impulse)
        {
            int thrown = Mathf.Min(count, _idle.Count);

            for (int i = 0; i < thrown; i++)
            {
                var piece = _idle.Dequeue();

                piece.transform.position = at + Random.insideUnitSphere * scatter;
                piece.transform.rotation = Random.rotation;
                piece.gameObject.SetActive(true);

                piece.velocity = Vector3.zero;
                piece.angularVelocity = Vector3.zero;

                Vector3 push = (direction + Random.insideUnitSphere * sidewaysShare).normalized * impulse;
                piece.AddForce(push, ForceMode.Impulse);
                piece.AddTorque(Random.insideUnitSphere * spin, ForceMode.Impulse);

                StartCoroutine(Reclaim(piece));
            }

            PlayPop(at);

            if (ImpactFX.Instance != null && shatterClips != null && shatterClips.Length > 0)
                ImpactFX.Instance.PlayClip(shatterClips[Random.Range(0, shatterClips.Length)], at, shatterVolume);
        }

        private void PlayPop(Vector3 at)
        {
            if (_pops.Count == 0) return;

            var pop = _pops.Dequeue();
            pop.transform.position = at;
            pop.gameObject.SetActive(true);
            pop.Play(true);
            StartCoroutine(ReclaimPop(pop));
        }

        private IEnumerator Reclaim(Rigidbody piece)
        {
            yield return new WaitForSeconds(lifetime);

            piece.velocity = Vector3.zero;
            piece.angularVelocity = Vector3.zero;
            piece.gameObject.SetActive(false);
            _idle.Enqueue(piece);
        }

        private IEnumerator ReclaimPop(ParticleSystem pop)
        {
            yield return new WaitForSeconds(popLifetime);

            pop.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pop.gameObject.SetActive(false);
            _pops.Enqueue(pop);
        }
    }
}
