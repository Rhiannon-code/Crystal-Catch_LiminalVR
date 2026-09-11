using System;
using UnityEngine;
using Liminal.SDK.VR.Avatars;
using Liminal.SDK.VR.Input;
using IntuitiveDesigns.CrystalCatch;

namespace IntuitiveDesigns.ShootingRange
{
    public class Pistol : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform visual;
        [SerializeField] private ParticleSystem muzzleFlash;

        [Header("Hold")]
        [SerializeField] private VRAvatarLimbType hand = VRAvatarLimbType.RightHand;
        [SerializeField] private bool followHandTransform = true;
        [SerializeField] private bool startHeld;
        [SerializeField] private Vector3 gripLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 gripLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 holsterLocalPosition = new Vector3(0.3f, 1.05f, 0.35f);
        [SerializeField] private Vector3 holsterLocalEuler = new Vector3(-20f, 0f, 0f);

        [Header("Fire (data)")]
        [SerializeField] private bool automatic;
        [SerializeField] private float fireInterval = 0.18f;
        [SerializeField] private float spreadDegrees = 0.4f;

        [Header("Magazine (data, 0 = unlimited)")]
        [SerializeField] private int magazineSize = 12;
        [SerializeField] private float reloadSeconds = 1.1f;

        [Header("Recoil (data, degrees)")]
        [SerializeField] private float recoilKick = 7f;
        [SerializeField] private float recoilRecovery = 14f;

        [Header("Audio")]
        [SerializeField] private AudioClip[] fireClips;
        [SerializeField] private AudioClip reloadClip;
        [SerializeField] private AudioClip[] emptyClips;
        [SerializeField, Range(0f, 1f)] private float fireVolume = 0.8f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.05f;

        [Header("Haptics")]
        [SerializeField, Range(0f, 1f)] private float fireHaptic = 0.75f;
        [SerializeField, Range(0f, 1f)] private float scatterHaptic = 1f;

        [Header("Desktop testing")]
        [SerializeField] private bool mouseFires = true;

        public bool IsHeld { get; private set; }
        public bool ExternallyDriven { get; set; }
        public VRAvatarLimbType Hand { get { return hand; } }
        public Vector3 GripPosition { get { return transform.position; } }
        public Transform Muzzle { get { return muzzle != null ? muzzle : transform; } }
        public int RoundsLeft { get { return magazineSize > 0 ? _rounds : -1; } }
        public int MagazineSize { get { return magazineSize; } }
        public bool IsReloading { get { return _reloadRemaining > 0f; } }

        public event Action<int, int> AmmoChanged;
        public event Action Fired;

        private AudioSource _audio;
        private AimSight _sight;
        private Quaternion _visualRest = Quaternion.identity;
        private float _cooldown;
        private float _recoil;
        private float _recoilVelocity;
        private float _reloadRemaining;
        private int _rounds;

        private void Awake()
        {
            IsHeld = startHeld;
            _rounds = magazineSize;

            if (visual != null) _visualRest = visual.localRotation;
            _sight = GetComponent<AimSight>();

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;   // It is in your own hand, panning your own gun sounds wrong
        }

        private void Start()
        {
            RaiseAmmo();
        }

        public void SetHeld(bool held)
        {
            IsHeld = held;
            _cooldown = 0f;
            _reloadRemaining = 0f;
        }

        public void AssignHand(VRAvatarLimbType limbType)
        {
            hand = limbType;
        }

        // The player is never slowed by slow motion, only the world is
        private void Update()
        {
            if (!IsHeld) { HolsterPose(); return; }

            if (followHandTransform && !ExternallyDriven) FollowHand();

            if (_cooldown > 0f) _cooldown -= Time.unscaledDeltaTime;
            TickReload();

            if (WantsToFire()) TryFire();
        }

        private void LateUpdate()
        {
            if (visual == null) return;

            _recoil = Mathf.SmoothDamp(_recoil, 0f, ref _recoilVelocity, 1f / Mathf.Max(0.01f, recoilRecovery),
                                       Mathf.Infinity, Time.unscaledDeltaTime);
            visual.localRotation = _visualRest * Quaternion.Euler(-_recoil, 0f, 0f);
        }

        private IVRAvatarHand HandRig()
        {
            var avatar = VRAvatar.Active;
            if (avatar == null) return null;

            if (avatar.PrimaryHand != null && avatar.PrimaryHand.LimbType == hand) return avatar.PrimaryHand;
            if (avatar.SecondaryHand != null && avatar.SecondaryHand.LimbType == hand) return avatar.SecondaryHand;
            return avatar.PrimaryHand;
        }

        private void FollowHand()
        {
            var rig = HandRig();
            if (rig == null || rig.Transform == null) { HolsterPose(); return; }

            transform.position = rig.Transform.TransformPoint(gripLocalPosition);
            transform.rotation = rig.Transform.rotation * Quaternion.Euler(gripLocalEuler);
        }

        private void HolsterPose()
        {
            var avatar = VRAvatar.Active;
            if (avatar == null || avatar.Head == null || avatar.Head.Transform == null) return;

            var head = avatar.Head.Transform;
            transform.position = head.TransformPoint(holsterLocalPosition);
            transform.rotation = head.rotation * Quaternion.Euler(holsterLocalEuler);
        }

        private bool WantsToFire()
        {
            var powerUps = PowerUps.Instance;
            bool auto = automatic || (powerUps != null && powerUps.FullAuto);

            var rig = HandRig();
            var device = rig != null ? rig.InputDevice : null;

            if (device != null)
            {
                if (auto ? device.GetButton(VRButton.Trigger) : device.GetButtonDown(VRButton.Trigger))
                    return true;
            }

            if (!mouseFires) return false;
            return auto ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        }

        private void TickReload()
        {
            if (_reloadRemaining <= 0f) return;

            _reloadRemaining -= Time.unscaledDeltaTime;
            if (_reloadRemaining > 0f) return;

            _reloadRemaining = 0f;
            _rounds = magazineSize;
            RaiseAmmo();
        }

        private void TryFire()
        {
            if (_cooldown > 0f || _reloadRemaining > 0f) return;

            var powerUps = PowerUps.Instance;
            bool bottomless = powerUps != null && powerUps.BottomlessMagazine;

            if (magazineSize > 0 && _rounds <= 0 && !bottomless) { BeginReload(); return; }

            int pellets = powerUps != null ? powerUps.Pellets : 1;
            float spread = pellets > 1 ? powerUps.PelletSpread : spreadDegrees;

            Transform from = Muzzle;
            var pool = ProjectilePool.Instance;
            Vector3 aim = _sight != null && pool != null
                        ? _sight.FireDirection(from.position, from.forward, pool.RoundSpeed, pool.RoundGravityScale)
                        : from.forward;
            int fired = 0;

            for (int i = 0; i < pellets; i++)
            {
                if (pool != null && pool.Fire(from.position, Scatter(aim, spread), 1f)) fired++;
            }

            _cooldown = fireInterval;

            if (fired == 0)
            {
                Play(Pick(emptyClips), 0.5f);
                return;
            }

            if (magazineSize > 0 && !bottomless) _rounds--;
            _recoil += recoilKick * (pellets > 1 ? 1.6f : 1f);

            if (muzzleFlash != null) muzzleFlash.Play(true);
            Play(Pick(fireClips), fireVolume);

            if (HapticPulse.Instance != null) HapticPulse.Instance.Hit(hand, pellets > 1 ? scatterHaptic : fireHaptic);

            RaiseAmmo();
            if (Fired != null) Fired();

            if (magazineSize > 0 && _rounds <= 0 && !bottomless) BeginReload();
        }

        private void BeginReload()
        {
            if (_reloadRemaining > 0f || magazineSize <= 0) return;

            _reloadRemaining = reloadSeconds;
            Play(Pick(emptyClips), 0.5f);
            Play(reloadClip, 0.7f);
            RaiseAmmo();
        }

        private static Vector3 Scatter(Vector3 forward, float degrees)
        {
            if (degrees <= 0f) return forward;
            return Quaternion.Euler(UnityEngine.Random.Range(-degrees, degrees),
                                    UnityEngine.Random.Range(-degrees, degrees),
                                    0f) * forward;
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null || _audio == null) return;

            _audio.pitch = 1f + (pitchJitter > 0f ? UnityEngine.Random.Range(-pitchJitter, pitchJitter) : 0f);
            _audio.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        private void RaiseAmmo()
        {
            if (AmmoChanged != null) AmmoChanged(_rounds, magazineSize);
        }

        private static AudioClip Pick(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[UnityEngine.Random.Range(0, clips.Length)];
        }
    }
}
