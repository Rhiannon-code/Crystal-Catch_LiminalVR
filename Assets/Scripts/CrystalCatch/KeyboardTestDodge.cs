using UnityEngine;

namespace IntuitiveDesigns.CrystalCatch
{
    public class KeyboardTestDodge : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Transform rig;

        [Header("Toggle")]
        [SerializeField] private bool enableDodgeTest = true;
        [SerializeField] private bool editorOnly = true;

        [Header("Reach (data, metres)")]
        [SerializeField] private float duckDrop = 0.55f;
        [SerializeField] private float leanReach = 0.5f;

        [Header("Feel")]
        [SerializeField] private float moveSpeed = 6f;

        [Header("Keys")]
        [SerializeField] private KeyCode duckKey = KeyCode.LeftControl;
        [SerializeField] private KeyCode duckKeyAlt = KeyCode.C;
        [SerializeField] private KeyCode leanLeftKey = KeyCode.A;
        [SerializeField] private KeyCode leanRightKey = KeyCode.D;

        private Vector3 _offset;

        private bool Active
        {
            get { return enableDodgeTest && (!editorOnly || Application.isEditor); }
        }

        private void LateUpdate()
        {
            if (!Active) return;

            if (rig == null)
            {
                var cam = Camera.main;
                if (cam == null) cam = Object.FindObjectOfType<Camera>();
                if (cam == null) return;

                var t = cam.transform;
                while (t.parent != null) t = t.parent;
                rig = t;
            }

            Vector3 target = Vector3.zero;

            if (Input.GetKey(duckKey) || Input.GetKey(duckKeyAlt)) target.y -= duckDrop;
            if (Input.GetKey(leanLeftKey) || Input.GetKey(KeyCode.LeftArrow)) target.x -= leanReach;
            if (Input.GetKey(leanRightKey) || Input.GetKey(KeyCode.RightArrow)) target.x += leanReach;

            float t2 = 1f - Mathf.Exp(-moveSpeed * Time.deltaTime);
            _offset = Vector3.Lerp(_offset, target, t2);

            // ADDITIVE, applied every frame. CartController plants the rig on the cart in Update,
            // so this has to be re-applied on top rather than assigned once
            rig.position += rig.rotation * _offset;
        }
    }
}
