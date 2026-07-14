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
        public const int CurrentVersion = 3;
        public const string CompletionPreferenceKey =
            "BrawlArena.GameplayCoach.CompletedVersion";

        static readonly GameplayCoachPage[] CoachPages =
        {
            new GameplayCoachPage(
                "MOVE",
                "LEFT SIDE / DRAG",
                "Drag the left joystick to move. Keep your distance from heavy hitters and use arena cover."),
            new GameplayCoachPage(
                "CAST",
                "RIGHT HALF / CAST + ORBIT",
                "Tap the right half for auto-aim; drag and release to cast manually. Right/middle-drag, or add a second touch, to orbit without casting."),
            new GameplayCoachPage(
                "WARD STEP",
                "RIGHT SIDE / TAP WARD STEP",
                "Move, then tap WARD STEP to commit a short escape or gap-close. Each step uses 20 of 60 Ward Flow."),
            new GameplayCoachPage(
                "RITUAL",
                "DEAL DAMAGE / CHARGE RITUAL",
                "Deal damage until RITUAL is ready. Tap for auto-aim, or drag and release for a directed Ritual."),
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
