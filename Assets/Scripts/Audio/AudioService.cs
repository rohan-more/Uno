using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Audio;

/// <summary>Sound effects and music, by way of Services.Audio.</summary>
public interface IAudioService
{
    /// <summary>Plays the sound for a moment. Does nothing if it has no clip, or before the library loads.</summary>
    void Play(Sfx sfx);

    /// <summary>Fades the current track out, then the new one in; None just fades out. The same track carries on.</summary>
    void PlayMusic(Music music);

    /// <summary>0-1, saved per player.</summary>
    float MusicVolume { get; set; }

    /// <summary>0-1, saved per player.</summary>
    float SfxVolume { get; set; }

    /// <summary>Loads the SoundLibrary. False if it couldn't; the game carries on silent.</summary>
    Task<bool> LoadAsync();
}

/// <summary>
/// Plays the game's sound. Effects go through a small pool of sources, so
/// overlapping sounds don't cut each other off. Music changes by fading the
/// old track out, then the new one in: two songs overlapping sounds like a clash.
///
/// Put it on the persistent Nakama object in the Startup scene, which lasts the
/// whole session, so music doesn't restart between scenes. It registers itself
/// as Services.Audio; the loading screen loads the SoundLibrary.
/// </summary>
public class AudioService : MonoBehaviour, IAudioService
{
    public const string LibraryAddress = "SoundLibrary";

    private const string MusicVolumeKey = "audio.musicVolume";
    private const string SfxVolumeKey = "audio.sfxVolume";

    [Tooltip("Optional: route through an AudioMixer.")]
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;

    [Tooltip("How many effects can play at once.")]
    [SerializeField] private int voices = 8;
    [Header("Music changes")]
    [Tooltip("How long the old track takes to fade out.")]
    [SerializeField] private float fadeOutSeconds = 1.2f;
    [Tooltip("How long the new track takes to fade in, after the old one has gone.")]
    [SerializeField] private float fadeInSeconds = 2.5f;

    private SoundLibrary library;
    private AudioSource[] sfxSources;
    private int nextVoice;
    private readonly System.Collections.Generic.Dictionary<Sfx, int> lastClip = new();

    private AudioSource musicSource;
    private Music currentMusic = Music.None;
    // The playing track's own volume, before MusicVolume. Looked up each time
    // rather than kept: editing the library in the Inspector replaces its
    // entries with new objects, and a kept one would never see the change.
    private float currentMusicVolume => library?.Get(currentMusic)?.volume ?? 0f;
    private Coroutine fade;

    private float musicVolume = 1f;
    private float sfxVolume = 1f;

    private void Awake()
    {
        // Services.Audio is an interface, so "!= null" can't see a destroyed one
        if (Services.Audio is AudioService existing && existing != null && existing != this)
        {
            Destroy(this);
            return;
        }

        DontDestroyOnLoad(gameObject);

        sfxSources = new AudioSource[Mathf.Max(1, voices)];
        for (int i = 0; i < sfxSources.Length; i++)
            sfxSources[i] = NewSource(sfxGroup, loop: false);
        musicSource = NewSource(musicGroup, loop: true);

        musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
        sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);

        Services.Register(this);
    }

    private void OnDestroy() => Services.Unregister(this);

    private AudioSource NewSource(AudioMixerGroup group, bool loop)
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f; // UI sound: no 3D
        source.outputAudioMixerGroup = group;
        return source;
    }

    public async Task<bool> LoadAsync()
    {
        if (library != null)
            return true;

        var handle = Addressables.LoadAssetAsync<SoundLibrary>(LibraryAddress);
        await handle.Task;
        library = handle.Result;
        if (library == null)
        {
            Debug.LogError($"Couldn't load the sound library from Addressables address '{LibraryAddress}'; playing silent");
            return false;
        }
        return true;
    }

    // ---- effects ----

    public void Play(Sfx sfx)
    {
        var entry = library != null ? library.Get(sfx) : null;
        if (entry == null || sfxVolume <= 0f)
            return;

        var source = sfxSources[nextVoice];
        nextVoice = (nextVoice + 1) % sfxSources.Length;

        source.Stop();
        source.clip = entry.clips[PickClip(sfx, entry.clips.Length)];
        source.volume = entry.volume * sfxVolume;
        source.pitch = 1f + Random.Range(-entry.pitchVariance, entry.pitchVariance);
        source.Play();
    }

    // A random clip, but not the one played last time when there's a choice
    private int PickClip(Sfx sfx, int count)
    {
        if (count <= 1)
            return 0;
        lastClip.TryGetValue(sfx, out var last);
        int pick = Random.Range(0, count - 1);
        if (pick >= last)
            pick++;
        lastClip[sfx] = pick;
        return pick;
    }

    // ---- music ----

    public void PlayMusic(Music music)
    {
        if (music == currentMusic)
            return; // already playing it, or already on the way to it
        currentMusic = music;

        // Starts from wherever the last change got to, so changing track again
        // mid-fade never jumps in volume.
        if (fade != null)
            StopCoroutine(fade);
        fade = StartCoroutine(ChangeTrack(music));
    }

    private IEnumerator ChangeTrack(Music music)
    {
        if (musicSource.isPlaying)
        {
            yield return FadeTo(0f, fadeOutSeconds);
            musicSource.Stop();
        }

        var entry = library != null ? library.Get(music) : null;
        if (entry != null)
        {
            musicSource.clip = entry.clip;
            musicSource.volume = 0f;
            musicSource.Play();
            yield return FadeTo(currentMusicVolume * musicVolume, fadeInSeconds);
        }
        fade = null;
    }

    // Eases rather than ramps, so the start and end of a fade aren't heard as a step
    private IEnumerator FadeTo(float target, float seconds)
    {
        float start = musicSource.volume;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            musicSource.volume = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        musicSource.volume = target;
    }

    // Follows the library's volume live, so a track can be balanced while it plays
    private void Update()
    {
        if (fade == null && musicSource != null && musicSource.isPlaying)
            musicSource.volume = currentMusicVolume * musicVolume;
    }

    // ---- settings ----

    public float MusicVolume
    {
        get => musicVolume;
        set
        {
            musicVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
            if (fade == null && musicSource.isPlaying)
                musicSource.volume = currentMusicVolume * musicVolume;
        }
    }

    public float SfxVolume
    {
        get => sfxVolume;
        set
        {
            sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
        }
    }
}
