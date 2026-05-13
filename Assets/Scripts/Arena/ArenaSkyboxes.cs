using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Central place to assign a skybox per arena. Looks for Resources/Skyboxes/{name}.
    /// If the material is missing, leaves the current skybox alone (silent fallback).
    /// </summary>
    public static class ArenaSkyboxes
    {
        public static void Apply(string skyboxName)
        {
            if (string.IsNullOrEmpty(skyboxName)) return;
            var mat = Resources.Load<Material>("Skyboxes/" + skyboxName);
            if (mat == null) return;
            RenderSettings.skybox = mat;
            DynamicGI.UpdateEnvironment();
        }
    }
}
