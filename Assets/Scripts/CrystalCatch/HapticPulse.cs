using System.Collections;
using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class HapticPulse : MonoBehaviour
    {
        public static HapticPulse Instance { get; private set; }

        [Header("Feel (data)")]
        [SerializeField] private float frequency = 1f;
        [SerializeField] private float hitSeconds = 0.07f;
        [SerializeField] private float minAmplitude = 0.45f;
        [SerializeField] private float maxAmplitude = 1f;
        [SerializeField] private bool enableHaptics = true;

        [Header("Both hands (data)")]
        [SerializeField, Range(0f, 1f)] private float offHandShare = 0.75f;

        [Header("Diagnostics")]
        [SerializeField] private bool logDeviceStateOnce = false;

        private int _leftToken;
        private int _rightToken;
        private bool _logged;

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

        public void HitBoth(VRAvatarLimbType swingingHand, float strength01)
        {
            if (!enableHaptics) return;

            float amplitude = Mathf.Lerp(minAmplitude, maxAmplitude, Mathf.Clamp01(strength01));

            Pulse(swingingHand, amplitude, hitSeconds);

            if (offHandShare <= 0.001f) return;

            var other = swingingHand == VRAvatarLimbType.LeftHand
                      ? VRAvatarLimbType.RightHand
                      : VRAvatarLimbType.LeftHand;

            Pulse(other, amplitude * offHandShare, hitSeconds);
        }

        public void Pulse(VRAvatarLimbType hand, float amplitude, float seconds)
        {
            if (!enableHaptics) return;

            bool left = hand == VRAvatarLimbType.LeftHand;
            var controller = left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;

            int token = left ? ++_leftToken : ++_rightToken;

            LogDeviceState();
            Vibrate(controller, frequency, Mathf.Clamp01(amplitude));
            LogPulse(controller, amplitude, seconds);
            StartCoroutine(StopAfter(controller, left, token, seconds));
        }

        private static void Vibrate(OVRInput.Controller controller, float freq, float amp)
        {
            if (OVRPlugin.initialized)
                OVRPlugin.SetControllerVibration((uint)controller, freq, amp);
            else
                OVRInput.SetControllerVibration(freq, amp, controller);
        }

        private void LogDeviceState()
        {
            if (!logDeviceStateOnce || _logged) return;
            _logged = true;
            Debug.Log("[Haptic] OVRPlugin.initialized=" + OVRPlugin.initialized +
                      " loadedXRDevice=" + OVRManager.loadedXRDevice +
                      " connected=" + OVRInput.GetConnectedControllers() +
                      " active=" + OVRInput.GetActiveController());
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

            Vibrate(controller, 0f, 0f);
        }

        [ContextMenu("Stop all vibration")]
        public void StopAll()
        {
            Vibrate(OVRInput.Controller.LTouch, 0f, 0f);
            Vibrate(OVRInput.Controller.RTouch, 0f, 0f);
        }
    }
}
