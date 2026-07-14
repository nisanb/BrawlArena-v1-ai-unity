using UnityEngine;

namespace BrawlArena
{
    /// <summary>
    /// Project-owned fallback clips for roster entries that do not author a
    /// character-specific attack or hit sound.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatAudioPalette", menuName = "Brawl Arena/Combat Audio Palette")]
    public sealed class CombatAudioPalette : ScriptableObject
    {
        [SerializeField] AudioClip defaultAttack;
        [SerializeField] AudioClip defaultHit;

        public AudioClip DefaultAttack => defaultAttack;
        public AudioClip DefaultHit => defaultHit;

#if UNITY_EDITOR
        public void ConfigureForEditor(AudioClip attack, AudioClip hit)
        {
            defaultAttack = attack;
            defaultHit = hit;
        }
#endif
    }

    /// <summary>
    /// Lazy Resources-backed resolver. Explicit roster clips always win; null
    /// entries fall back to the shared palette without scene or prefab changes.
    /// </summary>
    public static class CombatAudioDefaults
    {
        public const string ResourcePath = "CombatAudioPalette";

        static CombatAudioPalette palette;

        public static CombatAudioPalette Palette
        {
            get
            {
                if (palette == null)
                    palette = Resources.Load<CombatAudioPalette>(ResourcePath);
                return palette;
            }
        }

        public static AudioClip ResolveAttack(AudioClip rosterOverride)
        {
            if (rosterOverride != null) return rosterOverride;
            return Palette != null ? Palette.DefaultAttack : null;
        }

        public static AudioClip ResolveHit(AudioClip rosterOverride)
        {
            if (rosterOverride != null) return rosterOverride;
            return Palette != null ? Palette.DefaultHit : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRuntimeState()
        {
            palette = null;
        }
    }
}
