using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CrystalCoreFX : MonoBehaviour
    {
        [SerializeField] private CrystalSpawner spawner;
        [SerializeField] private Transform coreVisual;   // The mesh to punch (defaults to this transform)
        [SerializeField] private Renderer coreRenderer;

        [Header("Feel data")]
        [SerializeField] private float punchScale = 1.12f;
        [SerializeField] private float recover = 8f;      // How fast it settles back
        [SerializeField] private Color flashColour = new Color(0.6f, 0.9f, 1f);
        [SerializeField] private float flashBoost = 1.5f;

        private Vector3 _baseScale;
        private float _pulse;         // 0..1, decays
        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            if (coreVisual == null) coreVisual = transform;
            _baseScale = coreVisual.localScale;
            _mpb = new MaterialPropertyBlock();
        }

        private void OnEnable()  { if (spawner != null) spawner.Emitted += Pulse; }
        private void OnDisable() { if (spawner != null) spawner.Emitted -= Pulse; }

        private void Pulse() => _pulse = 1f;

        private void Update()
        {
            if (_pulse <= 0f) return;
            _pulse = Mathf.MoveTowards(_pulse, 0f, recover * Time.deltaTime);

            coreVisual.localScale = Vector3.Lerp(_baseScale, _baseScale * punchScale, _pulse);

            if (coreRenderer != null)
            {
                coreRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_EmissionColor", flashColour * (_pulse * flashBoost));
                coreRenderer.SetPropertyBlock(_mpb);
            }
        }
    }
}
