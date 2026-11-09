using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Every moment that can make a sound. Add new ones at the end, so saved entries keep their meaning.</summary>
public enum Sfx
{
    CardPlayed,
    CardDrawn,
    Skip,
    Reverse,
    DrawTwo,
    WildDrawFour,
    WildColor,
    YourTurn,
    TimerTick,
    Win,
    Lose,
    Coins,
    ButtonClick,
}

/// <summary>Background tracks. None fades the music out.</summary>
public enum Music
{
    None,
    Menu,
    Match,
}

/// <summary>
/// Which clips play for each moment, and how loud. One asset for the whole
/// game, loaded through Addressables (address "SoundLibrary"); the clips it
/// references come with it. Swap or add clips here without touching code. A
/// moment with no clips simply plays nothing.
/// </summary>
[CreateAssetMenu(fileName = "SoundLibrary", menuName = "Card Clash/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [Serializable]
    public class SoundEntry
    {
        public Sfx id;
        [Tooltip("One is picked at random each time, never the same one twice in a row.")]
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random pitch change either way, so repeats don't sound identical.")]
        [Range(0f, 0.3f)] public float pitchVariance = 0.05f;
    }

    [Serializable]
    public class MusicEntry
    {
        public Music id;
        [Tooltip("Loops. Set its import Load Type to Streaming.")]
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.6f;
    }

    [Header("Sound effects")]
    [Tooltip("One entry per moment: pick the moment in Id, then drag in its clips.")]
    [SerializeField] private SoundEntry[] sounds = Array.Empty<SoundEntry>();

    [Header("Music")]
    [Tooltip("One entry per track: pick Menu or Match in Id, then drag in the clip.")]
    [SerializeField] private MusicEntry[] musicTracks = Array.Empty<MusicEntry>();

    private Dictionary<Sfx, SoundEntry> soundLookup;
    private Dictionary<Music, MusicEntry> musicLookup;

    /// <summary>The entry for a moment, or null if it has no clips.</summary>
    public SoundEntry Get(Sfx id)
    {
        soundLookup ??= BuildLookup(sounds, e => e.id);
        return soundLookup.TryGetValue(id, out var entry) && entry.clips is { Length: > 0 } ? entry : null;
    }

    /// <summary>The entry for a track, or null if it has no clip.</summary>
    public MusicEntry Get(Music id)
    {
        musicLookup ??= BuildLookup(musicTracks, e => e.id);
        return musicLookup.TryGetValue(id, out var entry) && entry.clip != null ? entry : null;
    }

    private Dictionary<TKey, TEntry> BuildLookup<TKey, TEntry>(TEntry[] entries, Func<TEntry, TKey> key)
    {
        var lookup = new Dictionary<TKey, TEntry>();
        foreach (var entry in entries)
        {
            if (entry == null)
                continue;
            if (!lookup.TryAdd(key(entry), entry))
                Debug.LogWarning($"{name}: {key(entry)} is listed twice; the first one is used", this);
        }
        return lookup;
    }

    // Edited in the Inspector: rebuild the lookups next time they're used
    private void OnValidate()
    {
        soundLookup = null;
        musicLookup = null;
    }
}
