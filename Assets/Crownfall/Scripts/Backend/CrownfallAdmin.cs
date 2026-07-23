using UnityEngine;

namespace Crownfall.Backend
{
    /// A hidden in-game admin / live-ops console for managing a player's account
    /// on-device: set currencies and progression, unlock cosmetics, drive the
    /// quest state, and push/pull the cloud mirror. This is the "administrate it
    /// myself" surface — usable right on a phone in a TestFlight build.
    ///
    /// Access is gated so a normal player can't stumble into it: tap the
    /// top-right corner five times quickly to raise a PIN pad, enter the PIN, and
    /// the console unlocks for that device. In the Editor, F8 toggles it directly.
    /// All rendering is IMGUI Event-based so it works regardless of whether the
    /// project uses the legacy Input or the new Input System backend.
    public class CrownfallAdmin : MonoBehaviour
    {
        // Change this to your own before shipping a public build.
        const string Pin = "4271";
        const string UnlockedPref = "admin.unlocked.v1";

        bool open;
        bool pinPad;
        string entered = "";

        int cornerTaps;
        float lastTapTime;

        Vector2 scroll;
        GUIStyle box, label, btn, header, hot;
        float uiScale = 1f;

        void EnsureStyles()
        {
            uiScale = Mathf.Max(1f, Screen.width / 720f);
            int fs = Mathf.RoundToInt(20 * uiScale);

            box = new GUIStyle(GUI.skin.box);
            label = new GUIStyle(GUI.skin.label) { fontSize = fs, richText = true, wordWrap = true };
            btn = new GUIStyle(GUI.skin.button) { fontSize = fs, fixedHeight = 44 * uiScale };
            header = new GUIStyle(GUI.skin.label)
            { fontSize = Mathf.RoundToInt(24 * uiScale), fontStyle = FontStyle.Bold, richText = true };
            hot = null;
        }

        void OnGUI()
        {
            if (box == null) EnsureStyles();

            var e = Event.current;

            // Editor shortcut: F8 toggles the console outright.
            if (Application.isEditor && e.type == EventType.KeyDown && e.keyCode == KeyCode.F8)
            {
                open = !open;
                pinPad = false;
                e.Use();
            }

            // Corner-tap gesture (works with touch: IMGUI maps touch 0 to mouse).
            if (!open && e.type == EventType.MouseDown)
            {
                var corner = new Rect(Screen.width - 110 * uiScale, 0, 110 * uiScale, 110 * uiScale);
                if (corner.Contains(e.mousePosition))
                {
                    float now = Time.unscaledTime;
                    if (now - lastTapTime > 3f) cornerTaps = 0;
                    lastTapTime = now;
                    cornerTaps++;
                    if (cornerTaps >= 5)
                    {
                        cornerTaps = 0;
                        if (PlayerPrefs.GetInt(UnlockedPref, 0) == 1) { open = true; }
                        else { pinPad = true; entered = ""; }
                    }
                    e.Use();
                }
            }

            if (pinPad) DrawPinPad();
            else if (open) DrawConsole();
        }

        // ------------------------------------------------------------ PIN pad

        void DrawPinPad()
        {
            float w = 300 * uiScale, h = 380 * uiScale;
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("ADMIN PIN", header);
            GUILayout.Label(new string('*', entered.Length), header);
            GUILayout.Space(6);

            string[] rows = { "123", "456", "789", "C0K" };
            foreach (var row in rows)
            {
                GUILayout.BeginHorizontal();
                foreach (char c in row)
                {
                    if (GUILayout.Button(c.ToString(), btn, GUILayout.Height(50 * uiScale)))
                    {
                        if (c == 'C') entered = "";
                        else if (c == 'K')
                        {
                            if (entered == Pin)
                            {
                                PlayerPrefs.SetInt(UnlockedPref, 1);
                                PlayerPrefs.Save();
                                pinPad = false; open = true;
                            }
                            else entered = "";
                        }
                        else if (entered.Length < 8) entered += c;
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("cancel", btn)) { pinPad = false; entered = ""; }
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ console

        void DrawConsole()
        {
            float w = Mathf.Min(Screen.width - 20, 520 * uiScale);
            var r = new Rect(10, 10, w, Screen.height - 20);
            GUILayout.BeginArea(r, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("<color=#FFD34E>CROWNFALL ADMIN</color>", header);
            GUILayout.Label(
                $"backend: <b>{CrownfallServices.Status}</b>\n" +
                $"online: {CrownfallServices.Online}\n" +
                $"playerId: {CrownfallServices.PlayerId ?? "-"}\n" +
                $"rev: {PlayerPrefs.GetInt("cloud.rev", 0)}", label);

            Divider("CURRENCY");
            Row("Coins", "meta.coins", 100, 9999);
            Row("Gems", "meta.gems", 25, 9999);

            Divider("PROGRESSION");
            Row("Trophies", "meta.trophies", 50, 5000);
            Row("Level", "meta.level", 1, 30);
            if (GUILayout.Button("Reset XP to 0", btn)) SetMeta("meta.xp", 0);

            Divider("UNLOCKS");
            if (GUILayout.Button("Unlock ALL sigils", btn)) SetMeta("meta.sigilsOwned", 63);
            if (GUILayout.Button("Lock sigils (own default only)", btn)) SetMeta("meta.sigilsOwned", 0);

            Divider("QUESTS");
            if (GUILayout.Button("Complete all daily quests", btn)) CompleteQuests();
            if (GUILayout.Button("Reset daily quests", btn)) ResetQuests();

            Divider("GIFT");
            if (GUILayout.Button("Reset free-gift cooldown", btn))
            { PlayerPrefs.SetString("meta.lastGift", "0"); CrownfallMeta.Reload(); }

            Divider("CLOUD");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Push now", btn)) _ = CrownfallCloud.ForcePush();
            if (GUILayout.Button("Pull now", btn)) _ = CrownfallCloud.ForcePull();
            GUILayout.EndHorizontal();

            Divider("DANGER");
            if (GUILayout.Button("Wipe LOCAL account (keep cloud)", btn)) WipeLocal();
            if (GUILayout.Button("Sign out (test fresh device)", btn)) _ = CrownfallServices.SignOutAndForgetAsync();
            if (GUILayout.Button("Re-lock this console", btn))
            { PlayerPrefs.SetInt(UnlockedPref, 0); PlayerPrefs.Save(); open = false; }

            GUILayout.Space(8);
            if (GUILayout.Button("Close", btn)) open = false;

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ helpers

        void Divider(string title)
        {
            GUILayout.Space(6);
            GUILayout.Label("<color=#8FB4FF>— " + title + " —</color>", label);
        }

        /// A labelled stepper row with -, +, and a max/zero shortcut.
        void Row(string name, string key, int step, int max)
        {
            int cur = PlayerPrefs.GetInt(key, 0);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: <b>{cur}</b>", label, GUILayout.Width(200 * uiScale));
            if (GUILayout.Button("-" + step, btn, GUILayout.Width(70 * uiScale))) SetMeta(key, Mathf.Max(0, cur - step));
            if (GUILayout.Button("+" + step, btn, GUILayout.Width(70 * uiScale))) SetMeta(key, cur + step);
            if (GUILayout.Button("MAX", btn, GUILayout.Width(70 * uiScale))) SetMeta(key, max);
            GUILayout.EndHorizontal();
        }

        /// Write a meta.* key and re-hydrate CrownfallMeta so the cache, the UI,
        /// and (via the Changed subscription) the cloud all follow.
        void SetMeta(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            CrownfallMeta.Reload();
        }

        void CompleteQuests()
        {
            foreach (var q in CrownfallQuests.Defs)
                PlayerPrefs.SetInt("quests.p." + q.id, q.target);
            PlayerPrefs.Save();
            CrownfallQuests.Reload();
        }

        void ResetQuests()
        {
            foreach (var q in CrownfallQuests.Defs)
            {
                PlayerPrefs.SetInt("quests.p." + q.id, 0);
                PlayerPrefs.SetInt("quests.c." + q.id, 0);
            }
            PlayerPrefs.Save();
            CrownfallQuests.Reload();
        }

        /// Reset the local account to first-run defaults. The cloud copy is kept,
        /// so a subsequent Pull restores it — this tests the fresh-install path.
        void WipeLocal()
        {
            string[] wipe =
            {
                "meta.gems","meta.coins","meta.trophies","meta.level","meta.xp",
                "meta.selectedClass","meta.mode","meta.sigil","meta.sigilsOwned",
                "meta.hasProfile","meta.inboxRead","meta.lastGift","meta.playerName",
                "quests.day","quests.p.play","quests.p.win","quests.p.kills",
                "quests.c.play","quests.c.win","quests.c.kills",
                "trophyroad.claimed.50","trophyroad.claimed.100","trophyroad.claimed.200",
                "trophyroad.claimed.350","trophyroad.claimed.500","trophyroad.claimed.750",
                "trophyroad.claimed.1000","cloud.rev",
            };
            foreach (var k in wipe) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
            CrownfallMeta.Reload();
            CrownfallQuests.Reload();
        }
    }
}
