using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IntuitiveDesigns.CrystalCatch.EditorTools
{
    /// <summary>
    /// Repairs the Magic effects pack materials for our render setup.
    ///
    /// Why this exists: the active quality level is "Mobile", which has softParticles = 0 and
    /// shadows = 0. Nothing in the scene casts realtime shadows either (ADR 0008 baked lights),
    /// so the camera never renders _CameraDepthTexture. Materials that still ship with
    /// _SoftParticlesEnabled = 1 keep the _FADING_ON keyword compiled in and sample that
    /// missing depth texture, which collapses the fade term and makes the particles vanish or
    /// render as hard opaque quads. That is the "transparency is broken" symptom.
    ///
    /// Blend mode is NOT touched here — see FixAdditive() below, which is deliberately a
    /// separate, opt-in menu item so we never silently change the art direction.
    /// </summary>
    public static class CCParticleFix
    {
        private const string PackMaterials = "Assets/Magic effects pack/Materials";

        [MenuItem("Crystal Catch/Fix Particle Materials (soft particles)")]
        public static void FixSoftParticles()
        {
            var log = new StringBuilder("[CCParticleFix] soft-particle pass\n");
            int changed = 0;

            foreach (var mat in LoadPackMaterials())
            {
                if (!mat.HasProperty("_SoftParticlesEnabled")) continue;
                if (mat.GetFloat("_SoftParticlesEnabled") <= 0f) continue;

                mat.SetFloat("_SoftParticlesEnabled", 0f);

                // _FADING_ON drives BOTH soft particles and camera fading in Particles/Standard.
                // Only keep it if camera fading is genuinely still in use.
                bool cameraFading = mat.HasProperty("_CameraFadingEnabled")
                                    && mat.GetFloat("_CameraFadingEnabled") > 0f;

                if (cameraFading) mat.EnableKeyword("_FADING_ON");
                else mat.DisableKeyword("_FADING_ON");

                // Neutralise the fade params so nothing reads a stale near/far distance.
                if (mat.HasProperty("_SoftParticleFadeParams"))
                    mat.SetVector("_SoftParticleFadeParams", Vector4.zero);

                EditorUtility.SetDirty(mat);
                changed++;
                log.AppendLine("  fixed: " + mat.name + (cameraFading ? "  (kept _FADING_ON for camera fade)" : ""));
            }

            AssetDatabase.SaveAssets();
            log.AppendLine("  materials changed: " + changed);
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// Optional, opt-in. Many pack materials carry an HDR-ish tint (_Color well above 1)
        /// that only makes sense for an additive glow, yet blend as plain alpha, so bright
        /// effects composite as milky solid quads instead of glowing. This converts the
        /// over-bright ones to Additive. Run it only if you want that look.
        /// </summary>
        [MenuItem("Crystal Catch/Fix Particle Materials (additive glow) - OPTIONAL")]
        public static void FixAdditive()
        {
            var log = new StringBuilder("[CCParticleFix] additive pass\n");
            int changed = 0;

            foreach (var mat in LoadPackMaterials())
            {
                if (!mat.HasProperty("_Mode") || !mat.HasProperty("_Color")) continue;

                // Only touch Fade-mode materials that were authored over-bright.
                if (!Mathf.Approximately(mat.GetFloat("_Mode"), 2f)) continue;
                Color c = mat.GetColor("_Color");
                if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) <= 1.01f) continue;

                // Mirrors UnityEditor's StandardParticleShaderGUI additive setup.
                mat.SetFloat("_Mode", 4f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.One);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.DisableKeyword("_ALPHAMODULATE_ON");
                mat.renderQueue = (int)RenderQueue.Transparent;

                EditorUtility.SetDirty(mat);
                changed++;
                log.AppendLine("  additive: " + mat.name);
            }

            AssetDatabase.SaveAssets();
            log.AppendLine("  materials changed: " + changed);
            Debug.Log(log.ToString());
        }

        /// <summary>Reports what is currently wrong without editing anything.</summary>
        [MenuItem("Crystal Catch/Audit Particle Materials")]
        public static void Audit()
        {
            var log = new StringBuilder("[CCParticleFix] audit\n");

            foreach (var mat in LoadPackMaterials())
            {
                var flags = new List<string>();

                if (mat.HasProperty("_SoftParticlesEnabled") && mat.GetFloat("_SoftParticlesEnabled") > 0f)
                    flags.Add("SOFT-PARTICLES (breaks: no depth texture)");

                if (mat.HasProperty("_DistortionEnabled") && mat.GetFloat("_DistortionEnabled") > 0f)
                    flags.Add("DISTORTION (GrabPass, broken in single-pass stereo)");

                if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 1.01f
                        && mat.HasProperty("_Mode") && Mathf.Approximately(mat.GetFloat("_Mode"), 2f))
                        flags.Add("over-bright tint on alpha blend (no glow)");
                }

                if (flags.Count > 0)
                    log.AppendLine("  " + mat.name + ": " + string.Join(", ", flags.ToArray()));
            }

            Debug.Log(log.ToString());
        }

        private static IEnumerable<Material> LoadPackMaterials()
        {
            var guids = AssetDatabase.FindAssets("t:Material", new[] { PackMaterials });
            var mats = new List<Material>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) mats.Add(mat);
            }
            return mats;
        }
    }
}
