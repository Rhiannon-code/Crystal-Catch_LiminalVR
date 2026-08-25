using UnityEngine;
using Liminal.SDK.VR.Avatars;
using Liminal.SDK.VR.Input;

namespace IntuitiveDesigns.CrystalCatch
{
    [RequireComponent(typeof(BatSwinger))]
    public class PickaxePickup : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private BatSwinger pickaxe;

        [Header("Grab (data)")]
        [SerializeField] private float grabRadius = 0.35f;
        [SerializeField] private bool requireProximity = true;
        [SerializeField] private float dwellSeconds = 0f;
        [SerializeField] private float proximityGraceSeconds = 12f;
        [SerializeField] private float autoStartSeconds = 45f;

        [Header("Desktop testing")]
        [SerializeField] private bool editorMouseGrab = true;
        [SerializeField] private bool logPickup = true;

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

        private readonly float[] _dwell = new float[2];
        private float _waited;

        private void Awake()
        {
            if (pickaxe == null) pickaxe = GetComponent<BatSwinger>();
        }

        private void Start()
        {
            if (pickaxe != null) pickaxe.SetHeld(false);
        }

        private void Update()
        {
            if (Taken) return;

            _waited += Time.deltaTime;

            var avatar = VRAvatar.Active;
            if (avatar != null)
            {
                if (TryHand(avatar.PrimaryHand, 0)) return;
                if (TryHand(avatar.SecondaryHand, 1)) return;
            }

            if (editorMouseGrab && Application.isEditor && Input.GetMouseButtonDown(0))
            {
                // No tracked hand in the emulator, so fall back to whatever limb the bat is already
                // pointed at rather than guessing a side
                Take(pickaxe != null ? pickaxe.Hand : VRAvatarLimbType.RightHand, "editor mouse");
                return;
            }

            if (autoStartSeconds > 0f && _waited >= autoStartSeconds)
                Take(pickaxe != null ? pickaxe.Hand : VRAvatarLimbType.RightHand, "auto start");
        }

        /// Returns true if this hand took it
        private bool TryHand(IVRAvatarHand hand, int slot)
        {
            if (hand == null) return false;

            var handTransform = hand.Transform;
            if (handTransform == null) return false;

            bool near = !requireProximity ||
                        Vector3.Distance(handTransform.position, GrabPoint()) <= grabRadius;

            if (dwellSeconds > 0f && requireProximity && near)
            {
                _dwell[slot] += Time.deltaTime;
                if (_dwell[slot] >= dwellSeconds)
                {
                    Take(hand.LimbType, "hover");
                    return true;
                }
            }
            else if (!near)
            {
                _dwell[slot] = 0f;
            }

            // Reaching in is only required for a while. After that a press is a press, wherever the
            // hand is
            bool reachSatisfied = near || _waited >= proximityGraceSeconds;
            if (!reachSatisfied) return false;

            var device = hand.InputDevice;
            if (device == null) return false;

            for (int i = 0; i < GrabButtons.Length; i++)
            {
                if (!device.GetButtonDown(GrabButtons[i])) continue;

                Take(hand.LimbType, "grab");
                return true;
            }

            return false;
        }

        /// The middle of the shaft rather than the object's origin, so the reach target is the part
        /// of the pickaxe the player can actually see themselves grabbing
        private Vector3 GrabPoint()
        {
            if (pickaxe == null) return transform.position;
            return pickaxe.GripPosition;
        }

        private void Take(VRAvatarLimbType limb, string how)
        {
            if (Taken) return;
            Taken = true;
            HeldBy = limb;

            if (pickaxe != null)
            {
                pickaxe.AssignHand(limb);
                pickaxe.SetHeld(true);
            }

            if (game != null) game.BeginFromPickup();

            if (logPickup)
                Debug.Log("[PickaxePickup] Taken in the " + limb + " by " + how +
                          " after " + _waited.ToString("0.0") + " s. Cart rolling.");

            // Nothing left to watch for, and the button polling above is not free
            enabled = false;
        }

        /// For a test harness or a future "drop it" rule. Puts it back in the holster and re-arms
        [ContextMenu("Return to holster")]
        public void ReturnToHolster()
        {
            Taken = false;
            _waited = 0f;
            _dwell[0] = _dwell[1] = 0f;
            if (pickaxe != null) pickaxe.SetHeld(false);
            enabled = true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Taken ? Color.grey : Color.cyan;
            Gizmos.DrawWireSphere(GrabPoint(), grabRadius);
        }
#endif
    }
}
