using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class ProjectilePool : MonoBehaviour
    {
        public static ProjectilePool Instance { get; private set; }

        [SerializeField] private Projectile projectilePrefab;
        [SerializeField] private int poolSize = 32;
        [SerializeField] private bool warnWhenExhausted = true;

        public float RoundSpeed { get { return projectilePrefab != null ? projectilePrefab.Speed : 0f; } }
        public float RoundGravityScale { get { return projectilePrefab != null ? projectilePrefab.GravityScale : 0f; } }

        private readonly Queue<Projectile> _idle = new Queue<Projectile>();

        private void Awake()
        {
            Instance = this;
            if (projectilePrefab == null)
            {
                Debug.LogError("[ProjectilePool] No projectile prefab assigned. Nothing will fire.");
                return;
            }

            for (int i = 0; i < poolSize; i++)
            {
                var round = Instantiate(projectilePrefab, transform);
                round.gameObject.SetActive(false);
                _idle.Enqueue(round);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool Fire(Vector3 origin, Vector3 direction, float speedScale)
        {
            if (_idle.Count == 0)
            {
                if (warnWhenExhausted)
                    Debug.LogWarning("[ProjectilePool] Exhausted. Raise poolSize above fire rate x round lifetime.");
                return false;
            }

            _idle.Dequeue().Fire(origin, direction, speedScale);
            return true;
        }

        public void Return(Projectile round)
        {
            if (round != null) _idle.Enqueue(round);
        }
    }
}
