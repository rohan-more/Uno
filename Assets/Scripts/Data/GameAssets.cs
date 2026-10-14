using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

/// <summary>The game's art, by way of Services.Assets.</summary>
public interface IAssetService
{
    /// <summary>Card definitions and art; CardDatabase.CardBack is the shared back.</summary>
    CardDatabase Cards { get; }

    /// <summary>Avatar sprites, indexed by the server's avatar number.</summary>
    AvatarLibrary Avatars { get; }

    /// <summary>True once both libraries are loaded.</summary>
    bool Loaded { get; }

    /// <summary>Loads everything without blocking, reporting 0-1 as it goes. Safe to call more than once.</summary>
    Task PreloadAsync(IProgress<float> progress = null);
}

/// <summary>
/// The one place game art comes from: the card library (faces and the shared
/// back) and the avatar library, loaded through Addressables by address.
///
/// The loading screen preloads both, so nothing waits later. Reading
/// Cards or Avatars before that (e.g. pressing Play in MatchScene directly)
/// loads on the spot instead. They stay loaded for the app's lifetime.
///
/// Addresses: mark CardDatabase and AvatarLibrary Addressable with the names
/// below, each in a group with its sprite atlas.
///
/// Registered as Services.Assets at app start.
/// </summary>
public class GameAssets : IAssetService
{
    public const string CardsAddress = "CardDatabase";
    public const string AvatarsAddress = "AvatarLibrary";

    private CardDatabase cards;
    private AvatarLibrary avatars;

    /// <summary>Card definitions and art; CardDatabase.CardBack is the shared back.</summary>
    public CardDatabase Cards => cards != null ? cards : cards = LoadNow<CardDatabase>(CardsAddress);

    /// <summary>Avatar sprites, indexed by the server's avatar number.</summary>
    public AvatarLibrary Avatars => avatars != null ? avatars : avatars = LoadNow<AvatarLibrary>(AvatarsAddress);

    /// <summary>
    /// Loads both libraries without blocking, reporting 0-1 as it goes. Safe to
    /// call more than once.
    /// </summary>
    public async Task PreloadAsync(IProgress<float> progress = null)
    {
        var cardsLoad = cards == null ? Addressables.LoadAssetAsync<CardDatabase>(CardsAddress) : default;
        var avatarsLoad = avatars == null ? Addressables.LoadAssetAsync<AvatarLibrary>(AvatarsAddress) : default;

        // Report the two loads' combined progress until both are done
        while ((cardsLoad.IsValid() && !cardsLoad.IsDone) || (avatarsLoad.IsValid() && !avatarsLoad.IsDone))
        {
            progress?.Report((Progress(cardsLoad) + Progress(avatarsLoad)) / 2f);
            await Task.Yield();
        }

        if (cardsLoad.IsValid())
            cards = cardsLoad.Result;
        if (avatarsLoad.IsValid())
            avatars = avatarsLoad.Result;
        progress?.Report(1f);

        if (cards == null)
            Debug.LogError($"Couldn't load the card library from Addressables address '{CardsAddress}'");
        if (avatars == null)
            Debug.LogError($"Couldn't load the avatar library from Addressables address '{AvatarsAddress}'");
    }

    /// <summary>True once both libraries are loaded.</summary>
    public bool Loaded => cards != null && avatars != null;

    private static float Progress<T>(AsyncOperationHandle<T> handle) =>
        !handle.IsValid() || handle.IsDone ? 1f : handle.PercentComplete;

    private static T LoadNow<T>(string address) where T : Object
    {
        var asset = Addressables.LoadAssetAsync<T>(address).WaitForCompletion();
        if (asset == null)
            Debug.LogError($"Couldn't load '{address}' from Addressables");
        return asset;
    }
}
