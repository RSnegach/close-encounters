using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace CloseEncounters.UI
{
    /// <summary>
    /// Forwards scroll and drag events to the nearest parent ScrollRect.
    /// Attach to child elements (buttons, rows) inside a ScrollRect that
    /// block scroll with EventTrigger or other event-consuming components.
    /// Also provides PointerEnter/Exit callbacks without blocking scroll.
    /// </summary>
    public class ScrollForwarder : MonoBehaviour,
        IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        private ScrollRect _parentScroll;

        public System.Action onPointerEnter;
        public System.Action onPointerExit;

        private void Awake()
        {
            _parentScroll = GetComponentInParent<ScrollRect>();
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_parentScroll != null)
                _parentScroll.OnScroll(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_parentScroll != null)
                _parentScroll.OnBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_parentScroll != null)
                _parentScroll.OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_parentScroll != null)
                _parentScroll.OnEndDrag(eventData);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            onPointerEnter?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            onPointerExit?.Invoke();
        }
    }
}
