using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Crownfall.UI
{
    /// <summary>
    /// Lightweight UI tween engine. One hidden runner MonoBehaviour updates every
    /// active tween with unscaled time (menus run while timeScale==0 in pause).
    /// All helpers kill any prior tween on the same target+channel, so calls are
    /// idempotent — safe to fire from rapid button spam or state churn.
    /// </summary>
    public static class UiTween
    {
        public enum Ease { Linear, CubicOut, CubicInOut, BackOut, ElasticOut, BounceOut, QuadIn }

        class Tw
        {
            public UnityEngine.Object target;   // owner; tween dies if destroyed
            public int channel;                 // one live tween per (target,channel)
            public float t, dur, delay;
            public Ease ease;
            public Action<float> apply;         // receives eased 0..1
            public Action done;
            public bool dead;
        }

        const int ChScale = 1, ChMove = 2, ChFade = 3, ChRotate = 4, ChColor = 5, ChCount = 6, ChFill = 7;

        static readonly List<Tw> tweens = new List<Tw>(64);
        static Runner runner;

        class Runner : MonoBehaviour
        {
            void Update()
            {
                float dt = Time.unscaledDeltaTime;
                for (int i = tweens.Count - 1; i >= 0; i--)
                {
                    var tw = tweens[i];
                    if (tw.dead || tw.target == null) { tweens.RemoveAt(i); continue; }
                    if (tw.delay > 0f) { tw.delay -= dt; continue; }
                    tw.t += dt;
                    float n = tw.dur <= 0f ? 1f : Mathf.Clamp01(tw.t / tw.dur);
                    try { tw.apply(Evaluate(tw.ease, n)); }
                    catch (Exception) { tw.dead = true; }
                    if (n >= 1f)
                    {
                        tw.dead = true;
                        tweens.RemoveAt(i);
                        tw.done?.Invoke();
                    }
                }
            }
        }

        static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("UiTweenRunner");
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }

        static float Evaluate(Ease e, float n)
        {
            switch (e)
            {
                case Ease.CubicOut: { float p = 1f - n; return 1f - p * p * p; }
                case Ease.CubicInOut: return n < 0.5f ? 4f * n * n * n : 1f - Mathf.Pow(-2f * n + 2f, 3f) / 2f;
                case Ease.BackOut: { float c1 = 1.70158f, c3 = c1 + 1f, p = n - 1f; return 1f + c3 * p * p * p + c1 * p * p; }
                case Ease.ElasticOut:
                    if (n <= 0f) return 0f; if (n >= 1f) return 1f;
                    return Mathf.Pow(2f, -10f * n) * Mathf.Sin((n * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
                case Ease.BounceOut:
                {
                    const float n1 = 7.5625f, d1 = 2.75f;
                    if (n < 1f / d1) return n1 * n * n;
                    if (n < 2f / d1) { n -= 1.5f / d1; return n1 * n * n + 0.75f; }
                    if (n < 2.5f / d1) { n -= 2.25f / d1; return n1 * n * n + 0.9375f; }
                    n -= 2.625f / d1; return n1 * n * n + 0.984375f;
                }
                case Ease.QuadIn: return n * n;
                default: return n;
            }
        }

        static void Add(UnityEngine.Object target, int channel, float dur, float delay, Ease ease, Action<float> apply, Action done)
        {
            EnsureRunner();
            Kill(target, channel);
            var tw = new Tw { target = target, channel = channel, dur = dur, delay = delay, ease = ease, apply = apply, done = done };
            tweens.Add(tw);
            if (delay <= 0f && dur <= 0f) { /* applied next frame; fine */ }
        }

        static void Kill(UnityEngine.Object target, int channel)
        {
            for (int i = 0; i < tweens.Count; i++)
                if (!tweens[i].dead && tweens[i].target == target && tweens[i].channel == channel)
                    tweens[i].dead = true;
        }

        public static void KillAll(UnityEngine.Object target)
        {
            for (int i = 0; i < tweens.Count; i++)
                if (tweens[i].target == target) tweens[i].dead = true;
        }

        // ---- transforms -----------------------------------------------------

        public static void Scale(RectTransform rt, Vector3 from, Vector3 to, float dur, Ease ease = Ease.BackOut, float delay = 0f, Action done = null)
        {
            if (rt == null) return;
            rt.localScale = from;
            Add(rt, ChScale, dur, delay, ease, n => { if (rt != null) rt.localScale = Vector3.LerpUnclamped(from, to, n); }, done);
        }

        /// <summary>Pop-in: start squashed, overshoot to full size. The default "appear" juice.</summary>
        public static void PopIn(RectTransform rt, float dur = 0.32f, float delay = 0f, Action done = null)
            => Scale(rt, Vector3.one * 0.62f, Vector3.one, dur, Ease.BackOut, delay, done);

        /// <summary>Press feedback: quick squash and rebound.</summary>
        public static void Punch(RectTransform rt, float amount = 0.12f, float dur = 0.26f)
        {
            if (rt == null) return;
            Vector3 baseScale = Vector3.one;
            Add(rt, ChScale, dur, 0f, Ease.Linear, n =>
            {
                if (rt == null) return;
                float s = 1f - amount * Mathf.Sin(n * Mathf.PI) * (1f - n * 0.4f);
                rt.localScale = baseScale * s;
            }, () => { if (rt != null) rt.localScale = baseScale; });
        }

        public static void Move(RectTransform rt, Vector2 from, Vector2 to, float dur, Ease ease = Ease.CubicOut, float delay = 0f, Action done = null)
        {
            if (rt == null) return;
            rt.anchoredPosition = from;
            Add(rt, ChMove, dur, delay, ease, n => { if (rt != null) rt.anchoredPosition = Vector2.LerpUnclamped(from, to, n); }, done);
        }

        /// <summary>Slide in from an offset relative to the rect's resting position.</summary>
        public static void SlideIn(RectTransform rt, Vector2 offset, float dur = 0.38f, Ease ease = Ease.CubicOut, float delay = 0f, Action done = null)
        {
            if (rt == null) return;
            Vector2 rest = rt.anchoredPosition;
            Move(rt, rest + offset, rest, dur, ease, delay, done);
        }

        public static void Rotate(RectTransform rt, float fromZ, float toZ, float dur, Ease ease = Ease.CubicOut, float delay = 0f)
        {
            if (rt == null) return;
            Add(rt, ChRotate, dur, delay, ease, n => { if (rt != null) rt.localRotation = Quaternion.Euler(0, 0, Mathf.LerpUnclamped(fromZ, toZ, n)); }, null);
        }

        /// <summary>Endless slow spin (for glows/rays behind hero art). Duration is seconds per revolution.</summary>
        public static void SpinForever(RectTransform rt, float secondsPerRev = 14f)
        {
            if (rt == null) return;
            Add(rt, ChRotate, float.MaxValue, 0f, Ease.Linear, _ =>
            {
                if (rt != null) rt.Rotate(0, 0, -360f * Time.unscaledDeltaTime / secondsPerRev);
            }, null);
        }

        /// <summary>Gentle endless bob (floating badges, "tap to start" prompts).</summary>
        public static void BobForever(RectTransform rt, float pixels = 10f, float period = 1.6f)
        {
            if (rt == null) return;
            Vector2 rest = rt.anchoredPosition;
            float t0 = Time.unscaledTime;
            Add(rt, ChMove, float.MaxValue, 0f, Ease.Linear, _ =>
            {
                if (rt != null) rt.anchoredPosition = rest + new Vector2(0, Mathf.Sin((Time.unscaledTime - t0) / period * Mathf.PI * 2f) * pixels);
            }, null);
        }

        /// <summary>Endless pulse between two scales (ready-state glows, PLAY button).</summary>
        public static void PulseForever(RectTransform rt, float min = 0.97f, float max = 1.05f, float period = 1.2f)
        {
            if (rt == null) return;
            float t0 = Time.unscaledTime;
            Add(rt, ChScale, float.MaxValue, 0f, Ease.Linear, _ =>
            {
                if (rt != null)
                {
                    float s = Mathf.Lerp(min, max, 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime - t0) / period * Mathf.PI * 2f));
                    rt.localScale = Vector3.one * s;
                }
            }, null);
        }

        public static void StopLoop(RectTransform rt)
        {
            if (rt == null) return;
            Kill(rt, ChScale); Kill(rt, ChMove); Kill(rt, ChRotate);
            rt.localScale = Vector3.one;
        }

        // ---- graphics -------------------------------------------------------

        public static void Fade(CanvasGroup g, float from, float to, float dur, float delay = 0f, Action done = null)
        {
            if (g == null) return;
            g.alpha = from;
            Add(g, ChFade, dur, delay, Ease.CubicOut, n => { if (g != null) g.alpha = Mathf.LerpUnclamped(from, to, n); }, done);
        }

        public static void FadeGraphic(Graphic gr, float from, float to, float dur, float delay = 0f, Action done = null)
        {
            if (gr == null) return;
            var c = gr.color; c.a = from; gr.color = c;
            Add(gr, ChColor, dur, delay, Ease.CubicOut, n =>
            {
                if (gr == null) return;
                var cc = gr.color; cc.a = Mathf.LerpUnclamped(from, to, n); gr.color = cc;
            }, done);
        }

        public static void ColorTo(Graphic gr, Color from, Color to, float dur, float delay = 0f)
        {
            if (gr == null) return;
            gr.color = from;
            Add(gr, ChColor, dur, delay, Ease.CubicOut, n => { if (gr != null) gr.color = Color.LerpUnclamped(from, to, n); }, null);
        }

        public static void FillTo(Image img, float to, float dur = 0.45f, Ease ease = Ease.CubicOut)
        {
            if (img == null) return;
            float from = img.fillAmount;
            Add(img, ChFill, dur, 0f, ease, n => { if (img != null) img.fillAmount = Mathf.LerpUnclamped(from, to, n); }, null);
        }

        // ---- numbers --------------------------------------------------------

        /// <summary>Animated count-up on a label ("0" → "1,240"). Formatter defaults to plain int.</summary>
        public static void CountUp(TMP_Text label, int from, int to, float dur = 0.7f, Func<int, string> fmt = null, Action done = null)
        {
            if (label == null) return;
            fmt = fmt ?? (v => v.ToString());
            Add(label, ChCount, dur, 0f, Ease.CubicOut, n =>
            {
                if (label != null) label.text = fmt(Mathf.RoundToInt(Mathf.LerpUnclamped(from, to, n)));
            }, () => { if (label != null) { label.text = fmt(to); done?.Invoke(); } });
        }

        /// <summary>Typewriter reveal for flavor lines.</summary>
        public static void TypeText(TMP_Text label, string full, float charsPerSec = 40f, float delay = 0f)
        {
            if (label == null) return;
            label.text = full;
            label.maxVisibleCharacters = 0;
            float dur = full.Length / Mathf.Max(1f, charsPerSec);
            Add(label, ChCount, dur, delay, Ease.Linear, n =>
            {
                if (label != null) label.maxVisibleCharacters = Mathf.RoundToInt(n * full.Length);
            }, () => { if (label != null) label.maxVisibleCharacters = 99999; });
        }
    }
}
