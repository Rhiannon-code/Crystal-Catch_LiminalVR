using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class HeadLockedHud : MonoBehaviour
    {
        [Header("Placement (data)")]
        [SerializeField] private float distance = 1.8f;
        [SerializeField] private Vector3 localOffset = Vector3.zero;
        [SerializeField] private Transform headOverride;

        private bool _locked;

        private void LateUpdate()
        {
            if (_locked) return;

            // Resolved lazily and re-tried every frame until it lands. The SDK spawns the rig at
            // runtime, so on the first frames there is no head to parent to yet
            var head = ResolveHead();
            if (head == null) return;

            transform.SetParent(head, false);
            transform.localPosition = localOffset + new Vector3(0f, 0f, distance);
            transform.localRotation = Quaternion.identity;
            _locked = true;
        }

        private Transform ResolveHead()
        {
            if (headOverride != null) return headOverride;

            // Don't use ?. on VRAvatar, it's a UnityEngine.Object, so null propagation bypasses
            // Unity's overloaded == and would sail past a destroyed avatar
            var avatar = VRAvatar.Active;
            if (avatar != null)
            {
                var limb = avatar.GetLimb(VRAvatarLimbType.Head);
                if (limb != null) return limb.Transform;
            }

            var cam = Camera.main;
            if (cam == null) cam = Object.FindObjectOfType<Camera>();
            return cam != null ? cam.transform : null;
        }
    }
}
