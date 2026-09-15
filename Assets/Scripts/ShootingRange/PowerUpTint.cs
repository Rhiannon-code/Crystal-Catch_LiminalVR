using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class PowerUpTint : MonoBehaviour
    {
        [SerializeField] private PowerUps powerUps;
        [SerializeField] private Renderer[] renderers;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (powerUps == null) return;

            powerUps.Granted += OnGranted;
            powerUps.Expired += OnExpired;
            Refresh();
        }

        private void OnDisable()
        {
            if (powerUps == null) return;

            powerUps.Granted -= OnGranted;
            powerUps.Expired -= OnExpired;
        }

        private void OnGranted(PowerUpKind kind, float seconds)
        {
            Refresh();
        }

        private void OnExpired(PowerUpKind kind)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (renderers == null) return;

            PowerUpKind latest;
            bool tinted = powerUps.TryLatest(out latest);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                _block.Clear();
                if (tinted) _block.SetColor(ColorId, powerUps.Colour(latest));
                renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
