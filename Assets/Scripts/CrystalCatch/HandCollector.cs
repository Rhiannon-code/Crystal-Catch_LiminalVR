using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    [RequireComponent(typeof(SphereCollider))]
    public class HandCollector : MonoBehaviour
    {
        [SerializeField] private CrystalCatchGame game;
        [SerializeField] private VRAvatarLimbType hand = VRAvatarLimbType.RightHand;
        [SerializeField] private bool followHandTransform = true;

        [Header("Catch assist (data)")]
        [SerializeField] private float catchRadius = 0.06f;
        [SerializeField] private float impairedCatchRadius = 0.025f;
        [SerializeField] private float magnetRadius = 0.03f;
        [SerializeField] private float magnetSpeed = 20f;

        private Transform _hand;
        private SphereCollider _trigger;

        private void Awake()
        {
            _trigger = GetComponent<SphereCollider>();
            _trigger.isTrigger = true;
            _trigger.radius = catchRadius;
        }

        private void Update()
        {
            if (!followHandTransform) return;

            // VRAvatar.Active can be null until the SDK finishes spawning the rig, resolve lazily
            if (_hand == null)
            {
                var avatar = VRAvatar.Active;
                if (avatar == null) return;
                var limb = avatar.GetLimb(hand);
                if (limb == null) return;
                _hand = limb.Transform;
            }

            transform.SetPositionAndRotation(_hand.position, _hand.rotation);

            // Slow Time hazard shrinks the catch assist so catches demand precision
            if (_trigger != null)
                _trigger.radius = (game != null && game.HandsImpaired) ? impairedCatchRadius : catchRadius;
        }

        private void OnTriggerStay(Collider other)
        {
            // Magnet, ease a very close crystal into the palm for a satisfying, reliable snap
            if (magnetRadius <= 0f) return;
            if (game != null && game.HandsImpaired) return;   // No assist during Slow Time
            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d <= magnetRadius) return; // Already in the palm, OnTriggerEnter/this frame collects it
            if (d <= catchRadius && (other.GetComponent<Crystal>() != null || other.GetComponent<SpecialItem>() != null))
            {
                other.transform.position = Vector3.MoveTowards(
                    other.transform.position, transform.position, magnetSpeed * Time.deltaTime);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var crystal = other.GetComponent<Crystal>();
            if (crystal != null) { crystal.Collect(game); return; }

            var special = other.GetComponent<SpecialItem>();
            if (special != null) special.Collect(game);
        }
    }
}
