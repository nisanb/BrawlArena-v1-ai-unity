using UnityEngine;
using Crownfall.UI;

namespace Crownfall
{
    /// In-match meta UI: the pause button and the pause modal. The shop, inbox,
    /// gift, settings and battle-mode chooser live in the menu scene (MenuMeta).
    public partial class HUDController
    {
        GameObject pauseBtn;

        void BuildPause()
        {
            var btnImg = Icon("PauseBtn", root, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-24, -22), new Vector2(68, 68), btnCircle, Color.white);
            MakeClickable(btnImg, () => MatchManager.I?.TogglePause());
            Icon("L", btnImg.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0, 2), new Vector2(26, 26), icoPause, Color.white);
            pauseBtn = btnImg.gameObject;
            pauseBtn.SetActive(false);

            var frame = ModalShell("Paused", new Vector2(480, 470), out pauseModal, closable: false);
            MenuButton(frame, new Vector2(0, 68), new Vector2(350, 92), "RESUME", 32,
                btnGreen, icoPlay, () => MatchManager.I?.TogglePause());
            MenuButton(frame, new Vector2(0, -40), new Vector2(350, 84), "REMATCH", 26,
                btnBlue, icoRefresh, () => MatchManager.I?.Restart());
            MenuButton(frame, new Vector2(0, -140), new Vector2(350, 84), "HOME", 26,
                btnRed, icoHome, () => MatchManager.I?.QuitToMenu());
        }
    }
}
