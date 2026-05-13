using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CloseEncounters.UI
{
    /// <summary>
    /// Floating healthbar that hovers above a vehicle in world space.
    /// Created by ArenaManager for each vehicle. Shows bar + HP text.
    /// </summary>
    public class FloatingHealthbar : MonoBehaviour
    {
        private Transform _target;
        private Camera _cam;
        private RectTransform _rt;
        private RectTransform _fillRt;
        private Image _fillImage;
        private TMP_Text _hpText;
        private float _maxHp;
        private float _currentHp;
        private float _yOffset = 4f;

        public void Setup(Transform target, string displayName, float maxHp, float yOffset = 4f)
        {
            _target = target;
            _maxHp = maxHp;
            _currentHp = maxHp;
            _yOffset = yOffset;

            _rt = GetComponent<RectTransform>();
            if (_rt == null) _rt = gameObject.AddComponent<RectTransform>();
            _rt.sizeDelta = new Vector2(140f, 18f);

            // Background
            var bgGo = new GameObject("Bg", typeof(RectTransform));
            bgGo.transform.SetParent(transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.13f, 0.13f, 0.27f, 0.85f);

            // Fill
            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(transform, false);
            _fillRt = fillGo.GetComponent<RectTransform>();
            _fillRt.anchorMin = Vector2.zero;
            _fillRt.anchorMax = Vector2.one;
            _fillRt.offsetMin = Vector2.zero;
            _fillRt.offsetMax = Vector2.zero;
            _fillImage = fillGo.AddComponent<Image>();
            _fillImage.color = new Color(0.3f, 0.8f, 0.64f);

            // HP text overlay
            var textGo = new GameObject("HPText", typeof(RectTransform));
            textGo.transform.SetParent(transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            _hpText = textGo.AddComponent<TextMeshProUGUI>();
            _hpText.text = $"{(int)maxHp} / {(int)maxHp}";
            _hpText.fontSize = 12f;
            _hpText.color = Color.white;
            _hpText.alignment = TextAlignmentOptions.Center;
            _hpText.raycastTarget = false;

            // Name label above
            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(transform, false);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 1f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.pivot = new Vector2(0.5f, 0f);
            nameRt.anchoredPosition = new Vector2(0f, 2f);
            nameRt.sizeDelta = new Vector2(160f, 18f);
            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.text = displayName;
            nameTmp.fontSize = 13f;
            nameTmp.color = Color.white;
            nameTmp.alignment = TextAlignmentOptions.Center;
            nameTmp.raycastTarget = false;
        }

        public void ForceZero()
        {
            _currentHp = 0f;
            _fillRt.anchorMax = new Vector2(0f, 1f);
            _fillImage.color = new Color(0.91f, 0.27f, 0.38f);
            if (_hpText != null) _hpText.text = "0 / " + (int)_maxHp;
        }

        public void UpdateHP(float currentHp, float maxHp = -1f)
        {
            _currentHp = currentHp;
            if (maxHp > 0f) _maxHp = maxHp;
            if (_maxHp <= 0) return;

            float pct = Mathf.Clamp01(_currentHp / _maxHp);

            // Resize fill
            _fillRt.anchorMax = new Vector2(pct, 1f);

            // Color: green > 50%, yellow 25-50%, red < 25%
            if (pct > 0.5f)
                _fillImage.color = new Color(0.3f, 0.8f, 0.64f);
            else if (pct > 0.25f)
                _fillImage.color = new Color(0.94f, 0.75f, 0.25f);
            else
                _fillImage.color = new Color(0.91f, 0.27f, 0.38f);

            // Update text
            if (_hpText != null)
                _hpText.text = $"{(int)_currentHp} / {(int)_maxHp}";
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                if (_rt != null) _rt.anchoredPosition = new Vector2(-9999f, -9999f);
                return;
            }
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            Vector3 worldPos = _target.position + Vector3.up * _yOffset;
            Vector3 screenPos = _cam.WorldToScreenPoint(worldPos);

            if (screenPos.z < 0f)
            {
                _rt.anchoredPosition = new Vector2(-9999f, -9999f);
                return;
            }

            _rt.position = screenPos;
        }
    }
}
