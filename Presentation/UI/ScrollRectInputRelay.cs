using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LandLedgers.UI
{
    [DisallowMultipleComponent]
    public sealed class ScrollRectInputRelay : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private ScrollRect target;

        public static void Install(Component component, ScrollRect targetScroll)
        {
            if (component == null || targetScroll == null)
            {
                return;
            }

            ScrollRectInputRelay relay = component.GetComponent<ScrollRectInputRelay>() ?? component.gameObject.AddComponent<ScrollRectInputRelay>();
            relay.target = targetScroll;
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (eventData != null)
            {
                eventData.scrollDelta = new Vector2(0f, eventData.scrollDelta.y);
            }

            target?.OnScroll(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target == null)
            {
                return;
            }

            ClampToVerticalDrag(eventData);
            target.OnBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null)
            {
                return;
            }

            ClampToVerticalDrag(eventData);
            target.OnDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            target?.OnEndDrag(eventData);
        }

        private static void ClampToVerticalDrag(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            eventData.delta = new Vector2(0f, eventData.delta.y);
            eventData.position = new Vector2(eventData.pressPosition.x, eventData.position.y);
        }
    }
}
