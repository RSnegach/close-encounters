using System;
using UnityEngine;

namespace CloseEncounters.Core
{
    /// <summary>
    /// Service locator for cross-layer callbacks. UI implementations register themselves
    /// at Start; Gameplay/Combat/Arena call interface members without needing a direct
    /// type reference to UI. Breaks the Gameplay→UI cycle at the assembly boundary.
    /// All fields are optional — null-check before calling.
    /// </summary>
    public static class GameServices
    {
        public static IHUDService HUD;
        public static IMinimapService Minimap;
        public static IResultsService Results;

        public static void Reset()
        {
            HUD = null;
            Minimap = null;
            Results = null;
        }
    }

    public interface IHUDService
    {
        /// <summary>Append a short-lived entry to the kill feed, formatted by caller.</summary>
        void AddKillFeedEntry(string message);
        /// <summary>Trigger the fullscreen damage flash (red tint).</summary>
        void FlashDamage();
        /// <summary>Show the match-end "GAME OVER / VICTORY" label.</summary>
        void ShowGameOver(string text);
        /// <summary>Toggle whether floating healthbars over units are visible.</summary>
        bool HealthbarsVisible { get; set; }
    }

    public interface IMinimapService
    {
        /// <summary>Register a transform to be tracked by the minimap with a given color.</summary>
        void RegisterMarker(Transform t, Color color, float size);
        /// <summary>Unregister a previously-added marker.</summary>
        void UnregisterMarker(Transform t);
    }

    public interface IResultsService
    {
        /// <summary>Opaque payload so Gameplay doesn't need the concrete results type.</summary>
        void SetResults(object outcomePayload);
    }
}
