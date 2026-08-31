using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LandLedgers.UI
{
    [DisallowMultipleComponent]
    public sealed class UITooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private string tooltipTitle;
        [SerializeField, TextArea] private string tooltipBody;

        private Func<string> bodyProvider;
        private UITooltipController controller;

        public void Configure(string title, string body)
        {
            tooltipTitle = title ?? string.Empty;
            tooltipBody = body ?? string.Empty;
            bodyProvider = null;
        }

        public void Configure(string title, Func<string> provider)
        {
            tooltipTitle = title ?? string.Empty;
            tooltipBody = string.Empty;
            bodyProvider = provider;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Show();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Hide();
        }

        public void OnSelect(BaseEventData eventData)
        {
            Show();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            Hide();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void Show()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            var canvas = GetComponentInParent<Canvas>();
            controller ??= UITooltipController.GetOrCreate(canvas);
            controller?.Show(this, tooltipTitle, ResolveBody(), transform as RectTransform);
        }

        private void Hide()
        {
            controller?.Hide(this);
        }

        private string ResolveBody()
        {
            return bodyProvider != null ? bodyProvider.Invoke() : tooltipBody;
        }
    }
}
