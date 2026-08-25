using UnityEngine;
using Liminal.SDK.VR.Avatars;

namespace IntuitiveDesigns.CrystalCatch
{
    public class CaveAtmosphere : MonoBehaviour
    {
        [Header("Sight limit (data, metres)")]
        [SerializeField] private float sightLimit = 65f;
        [SerializeField] private float clearWithin = 28f;
        [SerializeField] private Color caveDark = new Color(0.10f, 0.082f, 0.072f);

        [Header("Camera")]
        [SerializeField] private bool clampFarClip = true;
        [SerializeField] private float farClipMargin = 6f;
        [SerializeField] private float drawMargin = 8f;

        public float SightLimit { get { return sightLimit; } }
        public float DrawDistance { get { return sightLimit + drawMargin; } }
        private Camera _camera;
        private float _nextSearch;
        private bool _hadFog;
        private FogMode _oldFogMode;
        private Color _oldFogColor;
        private float _oldFogStart;
        private float _oldFogEnd;
        private Material _oldSkybox;
        private UnityEngine.Rendering.AmbientMode _oldAmbientMode;
        private Color _oldAmbientLight;
        private bool _saved;

        private void Awake()
        {
            ApplyFog();
        }

        private void OnDestroy()
        {
            RestoreFog();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            ApplyFog();

            if (_camera != null && clampFarClip)
                _camera.farClipPlane = Mathf.Max(_camera.nearClipPlane + 1f, sightLimit + farClipMargin);
        }
#endif

        private void Update()
        {
            if (_camera != null && _camera.isActiveAndEnabled) return;

            if (Time.unscaledTime < _nextSearch) return;
            _nextSearch = Time.unscaledTime + 0.25f;

            _camera = ResolveCamera();
            if (_camera == null) return;

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = caveDark;

            if (clampFarClip)
                _camera.farClipPlane = Mathf.Max(_camera.nearClipPlane + 1f, sightLimit + farClipMargin);
        }

        [ContextMenu("Apply fog")]
        public void ApplyFog()
        {
            SaveFog();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = caveDark;
            RenderSettings.fogStartDistance = Mathf.Min(clearWithin, sightLimit - 1f);
            RenderSettings.fogEndDistance = sightLimit;

            // A skybox would be lit sky visible past the end of the tunnel. There is no sky in a mine
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = caveDark;
        }

        private void SaveFog()
        {
            if (_saved) return;
            _saved = true;

            _hadFog = RenderSettings.fog;
            _oldFogMode = RenderSettings.fogMode;
            _oldFogColor = RenderSettings.fogColor;
            _oldFogStart = RenderSettings.fogStartDistance;
            _oldFogEnd = RenderSettings.fogEndDistance;
            _oldSkybox = RenderSettings.skybox;
            _oldAmbientMode = RenderSettings.ambientMode;
            _oldAmbientLight = RenderSettings.ambientLight;
        }

        public void RestoreFog()
        {
            if (!_saved) return;
            _saved = false;

            RenderSettings.fog = _hadFog;
            RenderSettings.fogMode = _oldFogMode;
            RenderSettings.fogColor = _oldFogColor;
            RenderSettings.fogStartDistance = _oldFogStart;
            RenderSettings.fogEndDistance = _oldFogEnd;
            RenderSettings.skybox = _oldSkybox;
            RenderSettings.ambientMode = _oldAmbientMode;
            RenderSettings.ambientLight = _oldAmbientLight;
        }

        private Camera ResolveCamera()
        {
            // Don't use ?. on VRAvatar, it's a UnityEngine.Object, so null propagation bypasses
            // Unity's overloaded == and would sail past a destroyed avatar
            var avatar = VRAvatar.Active;
            if (avatar != null)
            {
                var head = avatar.GetLimb(VRAvatarLimbType.Head);
                if (head != null && head.Transform != null)
                {
                    var cam = head.Transform.GetComponentInChildren<Camera>();
                    if (cam != null) return cam;
                }
            }

            return Camera.main != null ? Camera.main : Object.FindObjectOfType<Camera>();
        }
    }
}
