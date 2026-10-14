using UnityEngine;

/// <summary>
/// Where scripts find the app's shared services, by interface, instead of each
/// class reaching for a singleton. A service may be null when its owner isn't
/// loaded (e.g. no connection while a scene runs on its own in the editor), so
/// callers check, as they always have.
///
/// Who registers what:
///   Assets      registered here at app start; it needs no scene.
///   Connection  NakamaConnection, in its Awake (Startup scene, lives all session).
///   Wallet      NakamaConnection too, alongside itself; it talks through it.
///   Popups      each scene's PopupManager, in its Awake; gone with the scene.
///
/// Every owner unregisters in OnDestroy, so a destroyed object never lingers
/// here (an interface can't be compared to null the way Unity objects can).
/// </summary>
public static class Services
{
    public static IConnectionService Connection { get; private set; }
    public static IWalletService Wallet { get; private set; }
    public static IAssetService Assets { get; private set; }
    public static IPopupService Popups { get; private set; }

    public static void Register(IConnectionService connection) => Connection = connection;
    public static void Register(IWalletService wallet) => Wallet = wallet;
    public static void Register(IAssetService assets) => Assets = assets;
    public static void Register(IPopupService popups) => Popups = popups;

    // Only the registered one is removed, so a duplicate being destroyed
    // doesn't take the real service with it.
    public static void Unregister(IConnectionService connection)
    {
        if (ReferenceEquals(Connection, connection))
            Connection = null;
    }
    

    public static void Unregister(IWalletService wallet)
    {
        if (ReferenceEquals(Wallet, wallet))
            Wallet = null;
    }

    public static void Unregister(IPopupService popups)
    {
        if (ReferenceEquals(Popups, popups))
            Popups = null;
    }

    // Runs before any scene loads, including with domain reload turned off,
    // so a previous Play session never leaks in.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Connection = null;
        Wallet = null;
        Popups = null;
        Assets = new GameAssets();
    }
}
