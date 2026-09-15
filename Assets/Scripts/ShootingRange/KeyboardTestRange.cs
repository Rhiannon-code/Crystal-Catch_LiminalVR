using UnityEngine;

namespace IntuitiveDesigns.ShootingRange
{
    public class KeyboardTestRange : MonoBehaviour
    {
        [SerializeField] private Arena arena;
        [SerializeField] private KeyCode restackKey = KeyCode.R;
        [SerializeField] private bool editorOnly = true;

        private void Update()
        {
            if (editorOnly && !Application.isEditor) return;

            if (arena != null && Input.GetKeyDown(restackKey)) arena.PresentRound(1);

            // Earning a power up takes an eight hit combo, which is no way to test one
            var powerUps = PowerUps.Instance;
            if (powerUps == null) return;

            for (int i = 0; i < PowerUps.KindCount; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) powerUps.Grant((PowerUpKind)i);
            }
        }
    }
}
