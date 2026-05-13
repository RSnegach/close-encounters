using UnityEngine;

namespace CloseEncounters.Arena
{
    /// <summary>
    /// Tag for world-space objective markers. MinimapController + (future) HUD
    /// layer pick these up and render indicators. Drop onto any GameObject worth
    /// showing to the player on the minimap or with a floating icon.
    /// </summary>
    public class WorldMarker : MonoBehaviour
    {
        public string label = "Objective";
        public Color color = new Color(1f, 0.85f, 0.1f, 1f);
        public float displayHeightOffset = 4f;
    }
}
