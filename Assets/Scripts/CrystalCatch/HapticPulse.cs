using System.Collections;
using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class HapticPulse : MonoBehaviour
    {
        public static HapticPulse Instance { get; private set; }

        [Header("Feel (data)")]
        [SerializeField] private float frequency = 0.7f;
        [SerializeField] private float hitSeconds = 0.07f;
        [SerializeField] private float minAmplitude = 0.45f;
        [SerializeField] private float maxAmplitude = 1f;
        [SerializeField] private bool enableHaptics = true;

        private int _leftToken;
        private int _rightToken;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            StopAll();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) StopAll();
        }

        public void Hit(VRAvatarLimbType hand, float strength01)
        {
            if (!enableHaptics) return;

            float amplitude = Mathf.Lerp(minAmplitude, maxAmplitude, Mathf.Clamp01(strength01));
            Pulse(hand, amplitude, hitSeconds);
        }

        public void Pulse(VRAvatarLimbType hand, float amplitude, float seconds)
        {
            if (!enableHaptics) return;

            bool left = hand == VRAvatarLimbType.LeftHand;
            var controller = left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

            int token = left ? ++_leftToken : ++_rightToken;

            OVRInput.SetControllerVibration(frequency, Mathf.Clamp01(amplitude), controller);
            LogPulse(controller, amplitude, seconds);
            StartCoroutine(StopAfter(controller, left, token, seconds));
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogPulse(OVRInput.Controller controller, float amplitude, float seconds)
        {
            Debug.Log("[Haptic] " + controller + " amp=" + amplitude.ToString("0.00") +
                      " for " + seconds.ToString("0.000") + "s");
        }

        private IEnumerator StopAfter(OVRInput.Controller controller, bool left, int token, float seconds)
        {
            yield return new WaitForSeconds(seconds);

            // A newer pulse has taken over this controller, so stopping now would cut it short
            if (token != (left ? _leftToken : _rightToken)) yield break;

            OVRInput.SetControllerVibration(0f, 0f, controller);
        }

        [ContextMenu("Stop all vibration")]
        public void StopAll()
        {
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }
    }
}
