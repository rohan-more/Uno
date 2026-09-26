using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The matchmaking panel: a 2x2 grid of four seats. Empty seats flick through
/// random avatars so it reads as "searching"; a seat stops on its real avatar
/// the moment the server says someone took it. The local player's seat is
/// filled from the start and never scrolls.
/// </summary>
public class MatchmakingPanel : MonoBehaviour
{
    [Serializable]
    public class Slot
    {
        public Image avatar;
        public TMP_Text nameText;
    }

    [Header("Seats (2x2: top-left, top-right, bottom-left, bottom-right)")]
    [SerializeField] private Slot[] slots = new Slot[4];


    [Header("Look")]
    [SerializeField] private AvatarLibrary avatars;
    [SerializeField] private string searchingLabel = "Searching…";
    [SerializeField] private int maxNameLength = 12;

    [Tooltip("Seconds between avatar changes while a seat is empty.")]
    [SerializeField] private float cycleInterval = 0.08f;

    [Header("Server")]
    [Tooltip("Fill seats from the server's LOBBY_STATE. Off means the panel only " +
             "does what the code calls directly, e.g. for offline UI work.")]
    [SerializeField] private bool listenToServer = true;

    [Tooltip("Which slot each seat goes in, relative to you. Entry 0 is your own " +
             "seat, entry 1 the player after you, and so on round the table.")]
    [SerializeField] private int[] slotOrder = { 2, 0, 1, 3 };

    [Header("Optional")]
    [SerializeField] private TMP_Text countdownText;

    [Tooltip("Fills the empty seats on a timer, to see the panel without a server.")]
    [SerializeField] private bool simulateForTesting;

    /// <summary>Raised when a seat stops scrolling, for a pop animation.</summary>
    public event Action<int> OnSlotFilled;

    private readonly bool[] _filled = new bool[4];
    private readonly float[] _nextCycle = new float[4];
    private int _lastSpriteIndex = -1;
    private float _simulateNextFill;

    private void Awake() => ClearSlots();

    private bool _subscribed;

    private void OnEnable() => TrySubscribe();

    private void OnDisable()
    {
        if (_subscribed && NakamaConnection.Instance != null)
            NakamaConnection.Instance.OnMatchState -= HandleMatchState;

        _subscribed = false;
    }

    /// <summary>
    /// The connection may not exist yet when this panel wakes up, so keep
    /// trying until it does rather than silently never receiving anything.
    /// </summary>
    private void TrySubscribe()
    {
        if (_subscribed || !listenToServer || NakamaConnection.Instance == null)
            return;

        NakamaConnection.Instance.OnMatchState += HandleMatchState;
        _subscribed = true;
    }

    /// <summary>
    /// Server messages. Only LOBBY_STATE matters here; the match scene handles
    /// the rest.
    /// </summary>
    private void HandleMatchState(long opCode, string json)
    {
        if (opCode != UnoOpCodes.LobbyState)
            return;

        var state = JsonUtility.FromJson<LobbyStateMsg>(json);
        if (state?.seats == null)
            return;

        ApplyLobbyState(state);
    }

    /// <summary>
    /// Shows the seats as the server sees them. Your own seat goes to the local
    /// slot and everyone else follows round the table, so each player sees
    /// themselves bottom-left.
    /// </summary>
    public void ApplyLobbyState(LobbyStateMsg state)
    {
        var mySeat = FindMySeat(state);

        foreach (var seat in state.seats)
        {
            var slot = SlotForSeat(seat.seat, mySeat);

            if (seat.IsEmpty)
                ClearSlot(slot); // keep this one scrolling: nobody there yet
            else
                FillSlot(slot, seat.name, seat.avatar);
        }

        SetCountdown(state.countdownMsLeft / 1000f);
    }

    /// <summary>Your seat number, or 0 if you can't be found in the list.</summary>
    private int FindMySeat(LobbyStateMsg state)
    {
        var myUserId = NakamaConnection.Instance != null ? NakamaConnection.Instance.UserId : null;
        if (string.IsNullOrEmpty(myUserId))
            return 0;

        foreach (var seat in state.seats)
        {
            if (seat.userId == myUserId)
                return seat.seat;
        }
        return 0;
    }

    /// <summary>
    /// Turns a server seat number into a grid slot, keeping turn order around
    /// the table: the player after you sits in slotOrder[1], and so on.
    /// </summary>
    private int SlotForSeat(int seat, int mySeat)
    {
        if (slotOrder == null || slotOrder.Length != slots.Length)
            return seat; // misconfigured: fall back to a direct mapping

        var offset = ((seat - mySeat) % slots.Length + slots.Length) % slots.Length;
        return slotOrder[offset];
    }

    /// <summary>
    /// Starts the search: your seat is shown at once, the rest start scrolling.
    /// </summary>
    public void BeginSearch(string localName, int localAvatarIndex)
    {
        ClearSlots();
        FillSlot(LocalSlot, localName, localAvatarIndex);

        if (simulateForTesting)
            _simulateNextFill = Time.time + 2f;
    }

    /// <summary>The slot showing you: the first entry of slotOrder, bottom-left by default.</summary>
    public int LocalSlot => slotOrder != null && slotOrder.Length > 0 ? slotOrder[0] : 2;

    /// <summary>Puts a slot back to scrolling, e.g. a seat nobody has taken yet.</summary>
    public void ClearSlot(int index)
    {
        if (!IsValid(index) || index == LocalSlot)
            return;

        _filled[index] = false;
        if (slots[index].nameText != null)
            slots[index].nameText.text = searchingLabel;
    }

    /// <summary>Stops a seat scrolling and shows who is sitting there.</summary>
    public void FillSlot(int index, string playerName, int avatarIndex)
    {
        if (!IsValid(index))
            return;

        var slot = slots[index];
        _filled[index] = true;

        if (slot.avatar != null && avatars != null)
            slot.avatar.sprite = avatars.Get(avatarIndex);

        if (slot.nameText != null)
            slot.nameText.text = LobbyView.Shorten(playerName, maxNameLength);

        OnSlotFilled?.Invoke(index);
    }

    /// <summary>Back to all seats empty and scrolling, except yours.</summary>
    public void ClearSlots()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            _filled[i] = false;
            _nextCycle[i] = 0f;

            if (slots[i]?.nameText != null)
                slots[i].nameText.text = searchingLabel;
        }

        SetCountdown(-1f);
    }

    /// <summary>Seconds until the match starts; a negative value hides the text.</summary>
    public void SetCountdown(float secondsLeft)
    {
        if (countdownText == null)
            return;

        countdownText.gameObject.SetActive(secondsLeft >= 0f);
        if (secondsLeft >= 0f)
            countdownText.text = Mathf.CeilToInt(secondsLeft).ToString();
    }

    private void Update()
    {
        TrySubscribe();

        if (avatars == null || avatars.Count == 0)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (_filled[i] || i == LocalSlot || slots[i]?.avatar == null)
                continue;

            if (Time.time < _nextCycle[i])
                continue;

            _nextCycle[i] = Time.time + cycleInterval;
            slots[i].avatar.sprite = avatars.Get(NextSpriteIndex());
        }

        if (simulateForTesting)
            SimulateFill();
    }

    /// <summary>A random avatar, never the same one twice in a row.</summary>
    private int NextSpriteIndex()
    {
        if (avatars.Count == 1)
            return 0;

        int index;
        do
        {
            index = UnityEngine.Random.Range(0, avatars.Count);
        } while (index == _lastSpriteIndex);

        _lastSpriteIndex = index;
        return index;
    }

    /// <summary>Editor-only stand-in for the server filling seats one by one.</summary>
    private void SimulateFill()
    {
        if (Time.time < _simulateNextFill)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (_filled[i])
                continue;

            FillSlot(i, "TestPlayer" + i, UnityEngine.Random.Range(0, Mathf.Max(1, avatars.Count)));
            _simulateNextFill = Time.time + 2f;
            return;
        }

        simulateForTesting = false; // table is full
    }

    private bool IsValid(int index) => index >= 0 && index < slots.Length && slots[index] != null;
}
