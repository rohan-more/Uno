using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>This player's coins, by way of Services.Wallet.</summary>
public interface IWalletService
{
    /// <summary>Raised whenever the balance or the free-coins wait changes.</summary>
    event Action OnChanged;

    /// <summary>Raised after OnChanged when the server just topped up free coins, with how many.</summary>
    event Action<long> OnFreeCoinsAdded;

    /// <summary>False until the server has answered once.</summary>
    bool Loaded { get; }

    long Coins { get; }

    /// <summary>Below the line, with a free top-up on its way.</summary>
    bool FreeCoinsWaiting { get; }

    /// <summary>How long until that top-up is due; zero once it is.</summary>
    TimeSpan FreeCoinsIn { get; }

    /// <summary>Free coins start below this balance...</summary>
    int FreeCoinsBelow { get; }

    /// <summary>...and top it up to this.</summary>
    int FreeCoinsUpTo { get; }

    /// <summary>What a match costs to enter. Below it you can't play.</summary>
    int Stake { get; }

    /// <summary>Enough coins for a match (true until the wallet has loaded, so nothing locks up early).</summary>
    bool CanAffordMatch { get; }

    /// <summary>Reads the balance. The server tops up free coins first if they're due.</summary>
    Task<bool> RefreshAsync();

    /// <summary>Testing: adds coins, or takes them with a negative amount. Refused unless the server's debug tools are on.</summary>
    Task<bool> DebugGrantAsync(int amount);

    /// <summary>Testing: if below the line, tops up free coins now.</summary>
    Task<bool> DebugFreeCoinsNowAsync();
}

/// <summary>
/// This player's coins, as the server last reported them. The server owns the
/// real balance: every method here asks it and then shows what it says, so the
/// client can never change coins by itself.
///
/// Free coins are added by the server whenever the wallet is read and they're
/// due; there is nothing to claim. A view showing the wait should refresh once
/// FreeCoinsIn reaches zero.
///
/// Registered as Services.Wallet by NakamaConnection. The loading screen
/// fetches it first; it refreshes itself after a reconnect. Subscribe to
/// OnChanged to show the balance.
/// </summary>
public class Wallet : IWalletService
{
    private readonly IConnectionService connection;

    // When the free top-up is due, on Unity's clock. Set from the server's
    // "in N ms", so the client never compares its clock with the server's.
    private float freeCoinsDueAt;

    public Wallet(IConnectionService connection)
    {
        this.connection = connection;

        // Coins may have moved while we were away (e.g. a match paid out)
        connection.OnReconnected += () => _ = RefreshAsync();
    }

    public event Action OnChanged;
    public event Action<long> OnFreeCoinsAdded;

    public bool Loaded { get; private set; }

    public long Coins { get; private set; }

    public bool FreeCoinsWaiting { get; private set; }

    public TimeSpan FreeCoinsIn => FreeCoinsWaiting
        ? TimeSpan.FromSeconds(Mathf.Max(0f, freeCoinsDueAt - Time.realtimeSinceStartup))
        : TimeSpan.Zero;

    public int FreeCoinsBelow { get; private set; }

    public int FreeCoinsUpTo { get; private set; }

    public int Stake { get; private set; }

    public bool CanAffordMatch => !Loaded || Coins >= Stake;

    public Task<bool> RefreshAsync() => CallAsync("get_wallet");

    public Task<bool> DebugGrantAsync(int amount) =>
        CallAsync("debug_grant_coins", JsonUtility.ToJson(new GrantRequest { amount = amount }));

    public Task<bool> DebugFreeCoinsNowAsync() => CallAsync("debug_free_coins_now");

    private async Task<bool> CallAsync(string rpc, string payload = "{}")
    {
        try
        {
            var response = await connection.RpcAsync(rpc, payload);
            var info = JsonUtility.FromJson<WalletResponse>(response.Payload);
            if (info == null)
                return false;
            Apply(info);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"{rpc} failed: {e.Message}");
            return false;
        }
    }

    private void Apply(WalletResponse info)
    {
        Coins = info.coins;
        FreeCoinsWaiting = info.freeCoinsWaiting;
        freeCoinsDueAt = Time.realtimeSinceStartup + info.freeCoinsInMs / 1000f;
        FreeCoinsBelow = info.freeCoinsBelow;
        FreeCoinsUpTo = info.freeCoinsUpTo;
        Stake = info.stake;
        Loaded = true;

        OnChanged?.Invoke();
        if (info.freeCoinsAdded > 0)
            OnFreeCoinsAdded?.Invoke(info.freeCoinsAdded);
    }

    [Serializable]
    private class WalletResponse
    {
        public long coins;
        public long freeCoinsAdded;
        public bool freeCoinsWaiting;
        public long freeCoinsInMs;
        public int freeCoinsBelow;
        public int freeCoinsUpTo;
        public int stake;
    }

    [Serializable]
    private class GrantRequest
    {
        public int amount;
    }
}
