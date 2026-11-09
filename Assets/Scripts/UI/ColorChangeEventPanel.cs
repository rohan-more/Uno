using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The wild panel: the screen washes in the colour chosen, and a card of that
/// colour flips in with its name on it ("BLUE"). Used for a Wild, and after
/// "Draw 4!" for a Wild Draw Four.
///
/// One card Image whose sprite is swapped per colour, so there's a single card
/// to lay out. The sprites come from the CardDatabase (its Colour cards), which
/// loads with the rest of the card art. The wash is a full-screen white Image;
/// it's tinted here.
/// </summary>
public class ColorChangeEventPanel : TableEventPanel
{
    [System.Serializable]
    public class ColorLook
    {
        public string name = "RED";
        [Tooltip("The wash and the name's colour.")]
        public Color tint = Color.red;
    }

    [Tooltip("A full-screen white Image behind the card.")]
    [SerializeField] private Image wash;
    [Range(0f, 1f)] [SerializeField] private float washAlpha = 0.35f;

    [SerializeField] private Image card;
    [Tooltip("Optional: the colour's name, over the card's white oval.")]
    [SerializeField] private TMP_Text colorName;
    [Tooltip("Colour the name in the chosen colour; off keeps the text's own colour.")]
    [SerializeField] private bool tintName = true;

    [Header("Colours")]
    [SerializeField] private ColorLook red = new() { name = "RED", tint = new Color(0.90f, 0.22f, 0.21f) };
    [SerializeField] private ColorLook yellow = new() { name = "YELLOW", tint = new Color(0.99f, 0.85f, 0.21f) };
    [SerializeField] private ColorLook green = new() { name = "GREEN", tint = new Color(0.26f, 0.63f, 0.28f) };
    [SerializeField] private ColorLook blue = new() { name = "BLUE", tint = new Color(0.12f, 0.53f, 0.90f) };

    [Header("Flip")]
    [SerializeField] private float flipDelay = 0.1f;
    [SerializeField] private float flipSeconds = 0.35f;
    [Tooltip("How tilted the card starts; it lands straight.")]
    [SerializeField] private float flipFromAngle = -15f;
    [SerializeField] private float namePopSeconds = 0.2f;

    protected override void Animate(Sequence sequence, TableEventContext context)
    {
        var look = LookFor(context.Color);
        if (look == null)
            return;

        if (wash != null)
        {
            var c = look.tint;
            c.a = washAlpha;
            wash.color = c;
        }

        if (colorName != null)
        {
            colorName.text = look.name;
            if (tintName)
                colorName.color = look.tint;
            var nameRect = colorName.rectTransform;
            nameRect.localScale = Vector3.zero;
            sequence.Insert(flipDelay + flipSeconds * 0.8f, nameRect.DOScale(1f, namePopSeconds).SetEase(Ease.OutBack));
        }

        if (card != null)
        {
            var sprite = Services.Assets?.Cards?.ColorCard(context.Color);
            if (sprite != null)
                card.sprite = sprite;

            // A flip: thin as an edge, turning to face us as it straightens
            var rect = card.rectTransform;
            rect.localScale = new Vector3(0f, 1f, 1f);
            rect.localRotation = Quaternion.Euler(0f, 0f, flipFromAngle);
            sequence.Insert(flipDelay, rect.DOScaleX(1f, flipSeconds).SetEase(Ease.OutBack));
            sequence.Insert(flipDelay, rect.DOLocalRotate(Vector3.zero, flipSeconds).SetEase(Ease.OutCubic));
        }
    }

    private ColorLook LookFor(CardColor color) => color switch
    {
        CardColor.Red => red,
        CardColor.Yellow => yellow,
        CardColor.Green => green,
        CardColor.Blue => blue,
        _ => null, // no colour chosen: nothing to show
    };
}
