using UnityEngine;
using Liminal.SDK.VR.Avatars;
using Liminal.SDK.VR.Input;

namespace IntuitiveDesigns.ShootingRange
{
    [RequireComponent(typeof(Pistol))]
    public class PistolPickup : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RangeGame game;
        [SerializeField] private Pistol pistol;

        [Header("Grab (data)")]
        [SerializeField] private float grabRadius = 0.35f;
        [SerializeField] private bool requireProximity = true;
        [SerializeField] private float proximityGraceSeconds = 12f;
        [SerializeField] private float autoStartSeconds = 45f;

        [Header("Desktop testing")]
        [SerializeField] private bool editorMouseGrab = true;

        public bool Taken { get; private set; }
        public VRAvatarLimbType HeldBy { get; private set; }

        private static readonly string[] GrabButtons =
        {
            VRButton.Trigger,
#if UNITY_XR
            VRButton.Seconday,
#else
            VRButton.Three,
#endif
        };

        private float _waited;

        private void Awake()
        {
            if (pistol == null) pistol = GetComponent<Pistol>();
        }

        private void Start()
        {
            if (pistol != null) pistol.SetHeld(false);
        }

        private void Update()
        {
            if (Taken) return;

            _waited += Time.deltaTime;

            var avatar = VRAvatar.Active;
            if (avatar != null)
            {
                if (TryHand(avatar.PrimaryHand)) return;
                if (TryHand(avatar.SecondaryHand)) return;
            }

            if (editorMouseGrab && Application.isEditor && Input.GetMouseButtonDown(0))
            {
                Take(pistol != null ? pistol.Hand : VRAvatarLimbType.RightHand, "editor mouse");
                return;
            }

            if (autoStartSeconds > 0f && _waited >= autoStartSeconds)
                Take(pistol != null ? pistol.Hand : VRAvatarLimbType.RightHand, "auto start");
        }

        private bool TryHand(IVRAvatarHand rig)
        {
            if (rig == null || rig.Transform == null) return false;

            bool near = !requireProximity ||
                        Vector3.Distance(rig.Transform.position, transform.position) <= grabRadius;

            // Reaching in is only required for a while. After that a press is a press, wherever the
            // hand is
            if (!near && _waited < proximityGraceSeconds) return false;

            var device = rig.InputDevice;
            if (device == null) return false;

            for (int i = 0; i < GrabButtons.Length; i++)
            {
                if (!device.GetButtonDown(GrabButtons[i])) continue;

                Take(rig.LimbType, "grab");
                return true;
            }

            return false;
        }

        private void Take(VRAvatarLimbType limb, string how)
        {
            if (Taken) return;

            Taken = true;
            HeldBy = limb;

            if (pistol != null)
            {
                pistol.AssignHand(limb);
                pistol.SetHeld(true);
            }

            if (game != null) game.BeginFromPickup();

            Debug.Log("[PistolPickup] Taken in the " + limb + " by " + how +
                      " after " + _waited.ToString("0.0") + " s.");

            enabled = false;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Taken ? Color.grey : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, grabRadius);
        }
#endif
    }
}
