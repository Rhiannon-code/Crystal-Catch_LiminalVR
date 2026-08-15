using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class HudFollower : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Transform cart;
        [SerializeField] private Transform headOverride;

        [Header("Placement (data)")]
        [SerializeField] private float distance = 2.0f;
        [SerializeField] private float heightOffset = -0.35f;

        [Header("Lazy follow")]
        [SerializeField] private float yawDeadzoneDegrees = 35f;]
        [SerializeField] private float followSmoothing = 3f;
        [SerializeField] private bool followHeadYaw = true;

        private Transform _head;
        private float _currentYaw;
        private bool _initialised;

        private void LateUpdate()
        {
            if (_head == null)
            {
                // Resolved lazily, the SDK spawns the rig at runtime, so nothing head related exists
                // on the first frames. The head camera IS the head transform, and using it avoids
                // depending on VRAvatar's head API shape
                if (headOverride != null)
                {
                    _head = headOverride;
                }
                else
                {
                    var cam = Camera.main;
                    if (cam == null) cam = Object.FindObjectOfType<Camera>();
                    if (cam == null) return;
                    _head = cam.transform;
                }
            }

            Vector3 anchor = cart != null ? cart.position : _head.position;

            float targetYaw = TargetYaw();

            if (!_initialised)
            {
                _currentYaw = targetYaw;
                _initialised = true;
            }
            else
            {
                float delta = Mathf.DeltaAngle(_currentYaw, targetYaw);

                // Inside the deadzone the panel simply does not move, which is what stops it swimming
                if (Mathf.Abs(delta) > yawDeadzoneDegrees)
                {
                    float excess = delta - Mathf.Sign(delta) * yawDeadzoneDegrees;
                    float t = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
                    _currentYaw = Mathf.MoveTowardsAngle(_currentYaw, _currentYaw + excess, Mathf.Abs(excess) * t);
                }
            }

            Quaternion yawRot = Quaternion.Euler(0f, _currentYaw, 0f);
            Vector3 forward = yawRot * Vector3.forward;

            // Height tracks the head so it stays right whether the player is standing or seated,
            // rather than assuming a fixed player height
            float y = _head.position.y + heightOffset;

            transform.position = new Vector3(anchor.x, y, anchor.z) + forward * distance;
            transform.rotation = yawRot;
        }

        private float TargetYaw()
        {
            if (followHeadYaw && _head != null) return _head.eulerAngles.y;
            if (cart != null) return cart.eulerAngles.y;
            return _currentYaw;
        }
    }
}
