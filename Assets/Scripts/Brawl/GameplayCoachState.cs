using System;
using UnityEngine;

namespace BrawlArena
{
    public readonly struct GameplayCoachPage
    {
        public string Title { get; }
        public string Control { get; }
        public string Body { get; }

        public GameplayCoachPage(string title, string control, string body)
        {
            Title = title;
            Control = control;
            Body = body;
        }
    }

    /// <summary>Versioned completion and readable content for first-match coaching.</summary>
    public static class GameplayCoachState
    {
        public const int CurrentVersion = 4;
        public const string CompletionPreferenceKey =
            "BrawlArena.GameplayCoach.CompletedVersion";

        static readonly GameplayCoachPage[] CoachPages =
        {
            new GameplayCoachPage(
                "MOVE",
                "LEFT SIDE / DRAG",
                "Drag the left joystick to move. Keep your distance from heavy hitters and use arena cover."),
            new GameplayCoachPage(
                "AIM & ATTACK",
                "RIGHT HALF / ATTACK + ORBIT",
                "Tap the right half to auto-aim; drag and release to attack in a chosen direction. Right/middle-drag, or add a second touch, to orbit without attacking."),
            new GameplayCoachPage(
                "DASH",
                "RIGHT SIDE / TAP DASH",
                "Move, then tap DASH to commit a short escape or gap-close. Each dash spends Energy that recharges over time."),
            new GameplayCoachPage(
                "SUPER",
                "DEAL DAMAGE / CHARGE SUPER",
                "Deal damage until SUPER is ready. Tap for auto-aim, or drag and release for a directed Super."),
        };

        public static int PageCount => CoachPages.Length;
        public static bool IsCompleted => ReadCompletedVersion() >= CurrentVersion;

        public static GameplayCoachPage GetPage(int index)
        {
            if (index < 0 || index >= CoachPages.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return CoachPages[index];
        }

        public static bool ShouldShow(bool automation)
        {
            return ShouldShowVersion(ReadCompletedVersion(), CurrentVersion, automation);
        }

        public static bool ShouldShowVersion(int completedVersion, int contentVersion,
            bool automation)
        {
            if (automation || contentVersion <= 0) return false;
            return completedVersion < contentVersion;
        }

        public static int ReadCompletedVersion()
        {
            try
            {
                return Mathf.Max(0, PlayerPrefs.GetInt(CompletionPreferenceKey, 0));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not load gameplay coach completion: " +
                                 exception.Message);
                return 0;
            }
        }

        public static bool MarkCompleted()
        {
            try
            {
                PlayerPrefs.SetInt(CompletionPreferenceKey, CurrentVersion);
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not save gameplay coach completion: " +
                                 exception.Message);
                return false;
            }
        }

        /// <summary>Queues the coach for the next non-automation match.</summary>
        public static bool RequestReplay()
        {
            return ResetCompletion();
        }

        public static bool ResetCompletion()
        {
            try
            {
                PlayerPrefs.DeleteKey(CompletionPreferenceKey);
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not reset gameplay coach completion: " +
                                 exception.Message);
                return false;
            }
        }
    }
}
