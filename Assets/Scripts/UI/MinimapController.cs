using UnityEngine;
using UnityEngine.UI;
using CloseEncounters.Arena;

namespace CloseEncounters.UI
{
    /// <summary>
    /// Minimap that renders a top-down camera to a RawImage in the HUD, with
    /// blips for the local player, other players, hostile units (warships,
    /// turrets, mines), and objective markers. Self-bootstrapping.
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        public float orthoSize = 180f;
        public int textureResolution = 256;
        public Vector2 panelSize = new Vector2(220f, 220f);

        private Camera _cam;
        private RenderTexture _rt;
        private RawImage _raw;
        private RectTransform _blipContainer;
        private Transform _player;
        private Image _playerBlip;

        private readonly System.Collections.Generic.List<(Transform target, Image blip, BlipKind kind)> _blips
            = new System.Collections.Generic.List<(Transform, Image, BlipKind)>();

        private enum BlipKind { Player, Enemy, Hostile, Objective }

        private void Start()
        {
            BuildCamera();
            BuildUI();
            ScanForEntities();
            InvokeRepeating(nameof(ScanForEntities), 2f, 3f);
        }

        private void LateUpdate()
        {
            if (_cam == null || _player == null)
            {
                FindPlayer();
                return;
            }
            Vector3 p = _player.position;
            _cam.transform.position = new Vector3(p.x, p.y + 120f, p.z);
            _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            for (int i = _blips.Count - 1; i >= 0; i--)
            {
                var b = _blips[i];
                if (b.target == null || b.blip == null) { if (b.blip != null) Destroy(b.blip.gameObject); _blips.RemoveAt(i); continue; }
                Vector3 rel = b.target.position - _cam.transform.position;
                float halfRange = _cam.orthographicSize;
                float u = Mathf.Clamp(rel.x / halfRange, -1f, 1f);
                float v = Mathf.Clamp(rel.z / halfRange, -1f, 1f);
                b.blip.rectTransform.anchoredPosition = new Vector2(u * panelSize.x * 0.5f, v * panelSize.y * 0.5f);
            }
        }

        private void BuildCamera()
        {
            var camGO = new GameObject("MinimapCam");
            camGO.transform.SetParent(transform, false);
            _cam = camGO.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = orthoSize;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.1f, 0.12f, 1f);
            _cam.cullingMask = ~0; // render everything
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 400f;
            _cam.depth = -1;

            _rt = new RenderTexture(textureResolution, textureResolution, 16, RenderTextureFormat.ARGB32);
            _cam.targetTexture = _rt;
        }

        private void BuildUI()
        {
            // Find a HUD canvas
            var canvas = GameObject.Find("HUD_Canvas") ?? GameObject.FindAnyObjectByType<Canvas>()?.gameObject;
            if (canvas == null) return;

            var panel = new GameObject("MinimapPanel");
            panel.transform.SetParent(canvas.transform, false);
            var panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = panelRT.anchorMax = new Vector2(0f, 0f);
            panelRT.pivot = new Vector2(0f, 0f);
            panelRT.anchoredPosition = new Vector2(16f, 16f);
            panelRT.sizeDelta = panelSize;

            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.6f);

            var rawGO = new GameObject("Raw");
            rawGO.transform.SetParent(panel.transform, false);
            var rawRT = rawGO.AddComponent<RectTransform>();
            rawRT.anchorMin = Vector2.zero;
            rawRT.anchorMax = Vector2.one;
            rawRT.offsetMin = new Vector2(4f, 4f);
            rawRT.offsetMax = new Vector2(-4f, -4f);
            _raw = rawGO.AddComponent<RawImage>();
            _raw.texture = _rt;

            var blipGO = new GameObject("Blips");
            blipGO.transform.SetParent(rawGO.transform, false);
            _blipContainer = blipGO.AddComponent<RectTransform>();
            _blipContainer.anchorMin = _blipContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _blipContainer.pivot = new Vector2(0.5f, 0.5f);
            _blipContainer.sizeDelta = panelSize;
            _blipContainer.anchoredPosition = Vector2.zero;
        }

        private void FindPlayer()
        {
            var ctrl = FindAnyObjectByType<CloseEncounters.Combat.PlayerVehicleController>();
            if (ctrl != null) _player = ctrl.transform;
        }

        private void ScanForEntities()
        {
            FindPlayer();
            // Players (use VehicleRuntime registry)
            var all = VehicleRuntime.LiveInstances;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                if (HasBlipFor(all[i].transform)) continue;
                Color c = all[i].IsAI ? new Color(1f, 0.3f, 0.2f, 1f) : new Color(0.2f, 1f, 0.4f, 1f);
                AddBlip(all[i].transform, c, 8f, BlipKind.Player);
            }
            // Hostile units
            foreach (var t in GameObject.FindObjectsByType<ShoreTurret>(FindObjectsSortMode.None))
                if (t != null && !HasBlipFor(t.transform)) AddBlip(t.transform, new Color(1f, 0.5f, 0.1f, 1f), 6f, BlipKind.Hostile);
            foreach (var w in GameObject.FindObjectsByType<Warship>(FindObjectsSortMode.None))
                if (w != null && !HasBlipFor(w.transform)) AddBlip(w.transform, new Color(1f, 0.2f, 0.1f, 1f), 9f, BlipKind.Hostile);
            foreach (var m in GameObject.FindObjectsByType<SeaMine>(FindObjectsSortMode.None))
                if (m != null && !HasBlipFor(m.transform)) AddBlip(m.transform, new Color(1f, 0.9f, 0.1f, 1f), 4f, BlipKind.Hostile);
            // Objective markers
            foreach (var wm in GameObject.FindObjectsByType<WorldMarker>(FindObjectsSortMode.None))
                if (wm != null && !HasBlipFor(wm.transform)) AddBlip(wm.transform, wm.color, 10f, BlipKind.Objective);
        }

        private bool HasBlipFor(Transform t)
        {
            for (int i = 0; i < _blips.Count; i++)
                if (_blips[i].target == t) return true;
            return false;
        }

        private void AddBlip(Transform target, Color c, float size, BlipKind kind)
        {
            if (_blipContainer == null) return;
            var go = new GameObject("Blip_" + target.name);
            go.transform.SetParent(_blipContainer, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.color = c;
            _blips.Add((target, img, kind));
        }
    }
}
