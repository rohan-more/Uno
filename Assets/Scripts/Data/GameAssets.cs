using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// The one place game art comes from: the card library (faces and the shared
/// back) and the avatar library, loaded through Addressables by address.
///
/// Boot preloads both while it connects, so nothing waits later. Reading
/// Cards or Avatars before that (e.g. pressing Play in MatchScene directly)
/// loads on the spot instead. They stay loaded for the app's lifetime.
///
/// Addresses: mark CardDatabase and AvatarLibrary Addressable with the names
/// below, each in a group with its sprite atlas.
/// </summary>
public static class GameAssets
{
    public const string CardsAddress = "CardDatabase";
    public const string AvatarsAddress = "AvatarLibrary";

    private static CardDatabase cards;
    private static AvatarLibrary avatars;

    /// <summary>Card definitions and art; CardDatabase.CardBack is the shared back.</summary>
    public static CardDatabase Cards => cards != null ? cards : cards = LoadNow<CardDatabase>(CardsAddress);

    /// <summary>Avatar sprites, indexed by the server's avatar number.</summary>
    public static AvatarLibrary Avatars => avatars != null ? avatars : avatars = LoadNow<AvatarLibrary>(AvatarsAddress);

    /// <summary>Loads both libraries without blocking. Safe to call more than once.</summary>
    public static async Task PreloadAsync()
    {
        var cardsLoad = cards == null ? Addressables.LoadAssetAsync<CardDatabase>(CardsAddress).Task : null;
        var avatarsLoad = avatars == null ? Addressables.LoadAssetAsync<AvatarLibrary>(AvatarsAddress).Task : null;

        if (cardsLoad != null)
            cards = await cardsLoad;
        if (avatarsLoad != null)
            avatars = await avatarsLoad;

        if (cards == null)
            Debug.LogError($"Couldn't load the card library from Addressables address '{CardsAddress}'");
        if (avatars == null)
            Debug.LogError($"Couldn't load the avatar library from Addressables address '{AvatarsAddress}'");
    }

    private static T LoadNow<T>(string address) where T : Object
    {
        var asset = Addressables.LoadAssetAsync<T>(address).WaitForCompletion();
        if (asset == null)
            Debug.LogError($"Couldn't load '{address}' from Addressables");
        return asset;
    }
}
