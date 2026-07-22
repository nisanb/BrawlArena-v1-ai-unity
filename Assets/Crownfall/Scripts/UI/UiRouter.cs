using System;
using System.Collections.Generic;
using UnityEngine;

namespace Crownfall.UI
{
    /// <summary>
    /// One navigable UI surface: a full screen or a modal. Built once at boot,
    /// toggled by the router. Hero is the rect that gets the pop-in juice;
    /// OnShow runs after activation for per-screen entrance choreography.
    /// </summary>
    public class UiPanel
    {
        public string Name;
        public GameObject Go;
        public CanvasGroup Group;
        public RectTransform Hero;
        public Action OnShow;
        public Action OnHide;
        /// <summary>Escape/back while this is the base screen. Return true if consumed.</summary>
        public Func<bool> OnBack;

        public bool Active => Go != null && Go.activeSelf;
    }

    /// <summary>
    /// Screen-stack navigation: exactly one base screen visible plus a stack of
    /// modals above it. All transitions are animated through UiTween and run on
    /// unscaled time so they work while the game is paused.
    /// </summary>
    public class UiRouter
    {
        public UiPanel Current { get; private set; }
        readonly List<UiPanel> modalStack = new List<UiPanel>();
        public event Action<UiPanel> ScreenChanged;

        const float InDur = 0.24f;
        const float OutDur = 0.13f;

        public void Show(UiPanel next)
        {
            if (Current == next && next != null && next.Active) return;
            var prev = Current;
            Current = next;
            if (prev != null && prev.Active && prev != next)
            {
                prev.OnHide?.Invoke();
                var dying = prev;
                UiTween.Fade(dying.Group, dying.Group != null ? dying.Group.alpha : 1f, 0f, OutDur,
                    0f, () => { if (Current != dying && !modalStack.Contains(dying)) dying.Go.SetActive(false); });
            }
            if (next != null)
            {
                next.Go.SetActive(true);
                if (next.Group != null) UiTween.Fade(next.Group, 0f, 1f, InDur);
                if (next.Hero != null) UiTween.PopIn(next.Hero);
                next.OnShow?.Invoke();
            }
            ScreenChanged?.Invoke(next);
        }

        public void OpenModal(UiPanel m)
        {
            if (m == null || m.Active) return;
            modalStack.Add(m);
            m.Go.transform.SetAsLastSibling();
            m.Go.SetActive(true);
            if (m.Group != null) UiTween.Fade(m.Group, 0f, 1f, InDur);
            if (m.Hero != null) UiTween.PopIn(m.Hero, 0.3f);
            m.OnShow?.Invoke();
        }

        public void CloseModal(UiPanel m)
        {
            if (m == null || !m.Active) return;
            modalStack.Remove(m);
            m.OnHide?.Invoke();
            if (m.Hero != null) UiTween.Scale(m.Hero, Vector3.one, Vector3.one * 0.82f, OutDur, UiTween.Ease.CubicOut);
            UiTween.Fade(m.Group, m.Group != null ? m.Group.alpha : 1f, 0f, OutDur, 0f, () =>
            {
                if (!modalStack.Contains(m)) m.Go.SetActive(false);
            });
        }

        public void CloseAllModals()
        {
            for (int i = modalStack.Count - 1; i >= 0; i--) CloseModal(modalStack[i]);
        }

        public bool IsModalOpen => modalStack.Count > 0;

        public bool IsOpen(UiPanel m) => m != null && m.Active;

        /// <summary>Escape/back: close the top modal first, else defer to the screen.</summary>
        public bool Back()
        {
            if (modalStack.Count > 0)
            {
                CloseModal(modalStack[modalStack.Count - 1]);
                return true;
            }
            return Current?.OnBack?.Invoke() ?? false;
        }
    }
}
