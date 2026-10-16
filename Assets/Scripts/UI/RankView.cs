using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row of the results screen: place in the star, cards left, name, coins
/// won and avatar. Put it on the RankRow prefab; ResultsView makes one per
/// player and fills it with Bind.
/// </summary>
public class RankView : MonoBehaviour
{
    [Tooltip("The number in the star: \"1\", or \"=2\" when the place is shared.")]
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image avatar;

    [Header("Avatar ring")]
    [Tooltip("Tinted by place. Works best with a white or light grey ring sprite.")]
    [SerializeField] private Image avatarRing;
    [Tooltip("1st, 2nd, 3rd, 4th. A shared place takes that place's colour.")]
    [SerializeField] private Color[] placeColors =
    {
        new(1f, 0.78f, 0.24f),    // gold
        new(0.79f, 0.83f, 0.87f), // silver
        new(0.80f, 0.50f, 0.20f), // bronze
        new(0.42f, 0.48f, 0.60f), // slate
    };

    [Header("Cards left")]
    [Tooltip("Shows 0 for the winner.")]
    [SerializeField] private TMP_Text cardCountText;

    [Header("Coins")]
    [SerializeField] private TMP_Text coinsText;
    [Tooltip("Optional: the coin icon and amount, hidden in a free match.")]
    [SerializeField] private GameObject coinGroup;
    [SerializeField] private float coinsCountSeconds = 0.8f;

    [Header("Row background")]
    [Tooltip("Optional: the row's background image, coloured to pick out your own row.")]
    [SerializeField] private Image rowBackground;
    [SerializeField] private Color yourRowColor = new(0.96f, 0.92f, 0.84f, 1f);
    [Tooltip("Alpha 0 hides the background on everyone else's row.")]
    [SerializeField] private Color otherRowColor = new(1f, 1f, 1f, 1f);

    private NumberCounter coins;
    private long coinsWon;

    /// <summary>Fills the row. Coins show 0 until CountCoins.</summary>
    public void Bind(int place, string rank, string playerName, Sprite avatarSprite, int cardsLeft, long coinsWon, bool staked, bool isYou)
    {
        SetText(rankText, rank);
        if (avatarRing != null && placeColors.Length > 0)
            avatarRing.color = placeColors[Mathf.Clamp(place - 1, 0, placeColors.Length - 1)];
        SetText(nameText, playerName);
        if (avatar != null && avatarSprite != null)
            avatar.sprite = avatarSprite;

        SetText(cardCountText, cardsLeft.ToString());

        this.coinsWon = coinsWon;
        coins = new NumberCounter(coinsText, CoinFormat.Short) { Seconds = coinsCountSeconds };
        coins.Set(0);
        if (coinGroup != null)
            coinGroup.SetActive(staked);
        else if (coinsText != null)
            coinsText.gameObject.SetActive(staked);

        if (rowBackground != null)
            rowBackground.color = isYou ? yourRowColor : otherRowColor;
    }

    /// <summary>Counts the coins up from 0 to what this place won.</summary>
    public void CountCoins() => coins?.CountTo(coinsWon);

    private void Update() => coins?.Tick();

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }
}
