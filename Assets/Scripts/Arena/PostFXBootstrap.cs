using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_RENDERING_URP || UNITY_URP
using UnityEngine.Rendering.Universal;
#endif

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Creates a runtime URP Volume with a basic bloom + ACES tonemapping + vignette
    /// + color-adjustments profile. Call once per arena in Build(). Safe to call
    /// multiple times — keeps only one bootstrap per scene.
    /// </summary>
    public static class PostFXBootstrap
    {
        public struct Settings
        {
            public float bloomIntensity;
            public Color colorFilter;
            public float saturation;      // -100..100
            public float contrast;        // -100..100
            public float vignetteIntensity;

            public static Settings Default => new Settings
            {
                bloomIntensity = 0.8f,
                colorFilter = Color.white,
                saturation = 0f,
                contrast = 0f,
                vignetteIntensity = 0.25f,
            };
        }

        public static void Apply(Settings settings)
        {
            var existing = GameObject.Find("[PostFX_Volume]");
            if (existing != null) Object.Destroy(existing);

            var go = new GameObject("[PostFX_Volume]");
            Object.DontDestroyOnLoad(go);
            var volume = go.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.priority = 1;

#if UNITY_RENDERING_URP || UNITY_URP || UNITY_2023_1_OR_NEWER
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(settings.bloomIntensity);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.7f);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var col = profile.Add<ColorAdjustments>(true);
            col.colorFilter.Override(settings.colorFilter);
            col.saturation.Override(settings.saturation);
            col.contrast.Override(settings.contrast);

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(settings.vignetteIntensity);
            vig.smoothness.Override(0.4f);
            vig.color.Override(Color.black);

            volume.sharedProfile = profile;
#endif
        }
    }
}
