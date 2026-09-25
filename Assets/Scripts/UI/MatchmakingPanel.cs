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

    /// <summary>Which slot shows you. Bottom-left in a top-left-first grid.</summary>
    [SerializeField] private int localSlotIndex = 2;

    [Header("Look")]
    [SerializeField] private AvatarLibrary avatars;
    [SerializeField] private string searchingLabel = "Searching…";
    [SerializeField] private int maxNameLength = 12;

    [Tooltip("Seconds between avatar changes while a seat is empty.")]
    [SerializeField] private float cycleInterval = 0.08f;

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

    /// <summary>
    /// Starts the search: your seat is shown at once, the rest start scrolling.
    /// </summary>
    public void BeginSearch(string localName, int localAvatarIndex)
    {
        ClearSlots();
        FillSlot(localSlotIndex, localName, localAvatarIndex);

        if (simulateForTesting)
            _simulateNextFill = Time.time + 2f;
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
        if (avatars == null || avatars.Count == 0)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            if (_filled[i] || i == localSlotIndex || slots[i]?.avatar == null)
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
