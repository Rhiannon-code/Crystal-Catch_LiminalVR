using System.Collections.Generic;
using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class PropStackBuilder : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Transform[] anchors;
        [SerializeField] private GameObject[] propPrefabs;
        [SerializeField] private GameObject capPrefab;

        [Header("Stack shape (data, metres)")]
        [SerializeField] private int baseWidth = 3;
        [SerializeField] private int rows = 3;
        [SerializeField] private float propSpacing = 0.18f;
        [SerializeField] private float rowHeight = 0.16f;
        [SerializeField] private float positionJitter = 0.006f;

        [Header("Build")]
        [SerializeField] private bool buildOnAwake = true;
        [SerializeField] private int seed = 20260908;

        public int StackCount { get { return _stacks.Count; } }

        private readonly List<GameObject> _stacks = new List<GameObject>();

        private void Awake()
        {
            if (buildOnAwake) Build();
        }

        [ContextMenu("Build")]
        public void Build()
        {
            Clear();

            if (anchors == null || anchors.Length == 0)
            {
                Debug.LogWarning("[PropStackBuilder] No anchors assigned. Nothing to stack.");
                return;
            }

            if (propPrefabs == null || propPrefabs.Length == 0)
            {
                Debug.LogWarning("[PropStackBuilder] No prop prefabs assigned. Nothing to stack.");
                return;
            }

            var random = new System.Random(seed);

            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] == null) continue;

                var root = new GameObject("Stack " + i);
                root.transform.SetParent(anchors[i], false);
                BuildStack(root.transform, random);
                _stacks.Add(root);
            }

            ActivateStacks(_stacks.Count);
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            for (int i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i] == null) continue;

                if (Application.isPlaying) Destroy(_stacks[i]);
                else DestroyImmediate(_stacks[i]);
            }

            _stacks.Clear();
        }

        public void ActivateStacks(int count)
        {
            for (int i = 0; i < _stacks.Count; i++)
            {
                if (_stacks[i] != null) _stacks[i].SetActive(i < count);
            }
        }

        private void BuildStack(Transform root, System.Random random)
        {
            for (int row = 0; row < rows; row++)
            {
                int count = baseWidth - row;
                if (count <= 0) break;

                float span = (count - 1) * propSpacing;

                for (int c = 0; c < count; c++)
                {
                    var local = new Vector3(c * propSpacing - span * 0.5f,
                                            row * rowHeight,
                                            0f);
                    local.x += Jitter(random);
                    local.z += Jitter(random);

                    Spawn(propPrefabs[random.Next(propPrefabs.Length)], root, local);
                }
            }

            if (capPrefab != null) Spawn(capPrefab, root, new Vector3(0f, rows * rowHeight, 0f));
        }

        private void Spawn(GameObject prefab, Transform root, Vector3 local)
        {
            if (prefab == null) return;

            var go = Instantiate(prefab, root);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.identity;

            // Awake already ran back at the prefab's own pose, so the rest pose has to be retaken
            // here or a reset would drop everything at the stack origin
            var knockable = go.GetComponent<Knockable>();
            if (knockable != null) knockable.CaptureRestPose();
        }

        private float Jitter(System.Random random)
        {
            if (positionJitter <= 0f) return 0f;
            return (float)(random.NextDouble() * 2.0 - 1.0) * positionJitter;
        }
    }
}
