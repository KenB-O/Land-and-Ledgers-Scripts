using System;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.UI
{
    public static class UITooltipRegistry
    {
        public static UITooltipTarget Attach(Button button, string title, string body)
        {
            return Attach(button as Component, title, body);
        }

        public static UITooltipTarget Attach(Button button, string title, Func<string> bodyProvider)
        {
            return Attach(button as Component, title, bodyProvider);
        }

        public static UITooltipTarget Attach(Component component, string title, string body)
        {
            if (component == null)
            {
                return null;
            }

            var target = GetOrAddTarget(component);
            target.Configure(title, body);
            return target;
        }

        public static UITooltipTarget Attach(Component component, string title, Func<string> bodyProvider)
        {
            if (component == null)
            {
                return null;
            }

            var target = GetOrAddTarget(component);
            target.Configure(title, bodyProvider);
            return target;
        }

        private static UITooltipTarget GetOrAddTarget(Component component)
        {
            if (component.TryGetComponent<UITooltipTarget>(out var existing))
            {
                return existing;
            }

            return component.gameObject.AddComponent<UITooltipTarget>();
        }
    }
}
