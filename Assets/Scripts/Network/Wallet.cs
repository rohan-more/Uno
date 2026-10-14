using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>This player's coins, by way of Services.Wallet.</summary>
public interface IWalletService
{
    /// <summary>Raised whenever the balance or the free-coins timer changes.</summary>
    event Action OnChanged;

    /// <summary>False until the server has answered once.</summary>
    bool Loaded { get; }

    long Coins { get; }

    /// <summary>How many coins a free claim gives.</summary>
    int FreeCoins { get; }

    bool FreeCoinsReady { get; }
    TimeSpan FreeCoinsIn { get; }

    Task<bool> RefreshAsync();

    /// <summary>Claims free coins. False if they weren't ready yet (the timer is updated either way).</summary>
    Task<bool> ClaimFreeCoinsAsync();

    /// <summary>Testing: adds coins. Refused unless the server's debug tools are on.</summary>
    Task<bool> DebugGrantAsync(int amount);

    /// <summary>Testing: makes free coins claimable now.</summary>
    Task<bool> DebugResetFreeCoinsAsync();
}

/// <summary>
/// This player's coins, as the server last reported them. The server owns the
/// real balance: every method here asks it and then shows what it says, so the
/// client can never change coins by itself.
///
/// Registered as Services.Wallet by NakamaConnection. The loading screen
/// fetches it first; it refreshes itself after a reconnect. Subscribe to
/// OnChanged to show the balance.
/// </summary>
public class Wallet : IWalletService
{
    private readonly IConnectionService connection;

    // When free coins become claimable, on Unity's clock. Set from the server's
    // "in N ms", so the client never compares its clock with the server's.
    private float freeCoinsReadyAt;

    public Wallet(IConnectionService connection)
    {
        this.connection = connection;

        // Coins may have moved while we were away (e.g. a match paid out)
        connection.OnReconnected += () => _ = RefreshAsync();
    }

    /// <summary>Raised whenever the balance or the free-coins timer changes.</summary>
    public event Action OnChanged;

    /// <summary>False until the server has answered once.</summary>
    public bool Loaded { get; private set; }

    public long Coins { get; private set; }

    /// <summary>How many coins a free claim gives.</summary>
    public int FreeCoins { get; private set; }

    public bool FreeCoinsReady => Loaded && Time.realtimeSinceStartup >= freeCoinsReadyAt;

    public TimeSpan FreeCoinsIn => TimeSpan.FromSeconds(Mathf.Max(0f, freeCoinsReadyAt - Time.realtimeSinceStartup));

    public Task<bool> RefreshAsync() => CallAsync("get_wallet");

    /// <summary>Claims free coins. False if they weren't ready yet (the timer is updated either way).</summary>
    public async Task<bool> ClaimFreeCoinsAsync()
    {
        var info = await CallInfoAsync("claim_free_coins");
        return info != null && info.claimed;
    }

    /// <summary>Testing: adds coins. Refused unless the server's debug tools are on.</summary>
    public Task<bool> DebugGrantAsync(int amount) =>
        CallAsync("debug_grant_coins", JsonUtility.ToJson(new GrantRequest { amount = amount }));

    /// <summary>Testing: makes free coins claimable now.</summary>
    public Task<bool> DebugResetFreeCoinsAsync() => CallAsync("debug_reset_free_coins");

    private async Task<bool> CallAsync(string rpc, string payload = "{}") =>
        await CallInfoAsync(rpc, payload) != null;

    private async Task<WalletResponse> CallInfoAsync(string rpc, string payload = "{}")
    {
        try
        {
            var response = await connection.RpcAsync(rpc, payload);
            var info = JsonUtility.FromJson<WalletResponse>(response.Payload);
            if (info != null)
                Apply(info);
            return info;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{rpc} failed: {e.Message}");
            return null;
        }
    }

    private void Apply(WalletResponse info)
    {
        Coins = info.coins;
        FreeCoins = info.freeCoins;
        freeCoinsReadyAt = Time.realtimeSinceStartup + info.freeCoinsInMs / 1000f;
        Loaded = true;
        OnChanged?.Invoke();
    }

    [Serializable]
    private class WalletResponse
    {
        public long coins;
        public int freeCoins;
        public long freeCoinsInMs;
        public bool claimed;
    }

    [Serializable]
    private class GrantRequest
    {
        public int amount;
    }
}
