using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// The Draw Two / Draw Four panel: a stack of cards fans out into 2 (or 4),
/// then the text pops. One class for both: one card image and a count, and it
/// makes the copies (or a list of separate cards, if they should differ).
///
/// The fan is an arc, like your hand (CurvedHandLayout): each card sits on a
/// circle and tilts to follow it, centred on where the cards are placed in the
/// editor. Or untick Use Arc to fan out to the editor layout exactly. Cards
/// start stacked in the middle and fan out one after another. Leave the panel's
/// own pop small (Start Scale 0.8-1), so the fan is what moves.
/// </summary>
public class DrawCardsEventPanel : TableEventPanel
{
    [Tooltip("One card image; the panel copies it to make Card Count cards.")]
    [SerializeField] private RectTransform cardTemplate;
    [Tooltip("How many cards fan out: 2 for Draw Two, 4 for Draw Four.")]
    [Min(1)] [SerializeField] private int cardCount = 2;
    [Tooltip("Optional, instead of the template: separate cards, left to right, for when they " +
             "should look different. Used if filled in.")]
    [SerializeField] private RectTransform[] cards;
    [Tooltip("Optional: e.g. \"Pickup 2\", popped once the cards have fanned out.")]
    [SerializeField] private RectTransform label;
    [Tooltip("Optional: the label's text, to show the total owed. Leave Label Format empty to keep its own text.")]
    [SerializeField] private TMP_Text labelText;
    [Tooltip("e.g. \"Pickup {0}\": {0} is the total owed, stacked cards included.")]
    [SerializeField] private string labelFormat = "";

    [Header("Stacked")]
    [Tooltip("Optional: shown over the cards when Draw cards were stacked, e.g. \"x2\".")]
    [SerializeField] private TMP_Text multiplierText;
    [Tooltip("{0} is how many were stacked: 2 for a Draw Four played on a Draw Four.")]
    [SerializeField] private string multiplierFormat = "x{0}";
    [SerializeField] private float multiplierPunch = 0.35f;

    [Header("Arc")]
    [Tooltip("Fan out along an arc like the hand. Off: fan out to where the cards sit in the editor.")]
    [SerializeField] private bool useArc = true;
    [Tooltip("The arc's radius: smaller is more curved.")]
    [SerializeField] private float arcRadius = 450f;
    [Tooltip("Degrees between neighbouring cards, at most (Max Fan Width can tighten it).")]
    [SerializeField] private float arcSpacing = 12f;
    [Tooltip("The farthest apart the outermost cards' centres may be, so the fan stays inside " +
             "its panel; the spacing tightens to fit. 0 = no limit.")]
    [SerializeField] private float maxFanWidth = 200f;

    [Header("Fan")]
    [Tooltip("Where the stack sits before fanning, relative to the cards' average position.")]
    [SerializeField] private Vector2 stackOffset = Vector2.zero;
    [Tooltip("A small random-looking tilt for each card in the stack, so it reads as a pile.")]
    [SerializeField] private float stackTilt = 4f;
    [Tooltip("When the fan starts, after the panel appears.")]
    [SerializeField] private float fanDelay = 0.12f;
    [SerializeField] private float fanSeconds = 0.3f;
    [Tooltip("Gap between one card leaving the stack and the next.")]
    [SerializeField] private float stagger = 0.06f;
    [SerializeField] private Ease fanEase = Ease.OutBack;

    [Header("Label")]
    [SerializeField] private float labelPopSeconds = 0.25f;

    private Vector2[] restPositions;
    private Quaternion[] restRotations;
    private Vector2 stackPoint;
    private Vector2 middle;

    // Copies the template into Card Count cards, once. The template is the
    // leftmost; each copy goes just after the one before it, so the right-hand
    // card draws on top, like a hand.
    private void BuildCards()
    {
        if (cards is { Length: > 0 } || cardTemplate == null)
            return;

        cards = new RectTransform[Mathf.Max(1, cardCount)];
        cards[0] = cardTemplate;
        int sibling = cardTemplate.GetSiblingIndex();
        for (int i = 1; i < cards.Length; i++)
        {
            var copy = Instantiate(cardTemplate, cardTemplate.parent);
            copy.name = $"{cardTemplate.name} ({i + 1})";
            copy.SetSiblingIndex(sibling + i);
            cards[i] = copy;
        }
    }

    // The editor layout is the fanned shape: remember it before moving anything
    private void Remember()
    {
        BuildCards();
        if (restPositions != null || cards == null)
            return;

        int n = cards.Length;
        restPositions = new Vector2[n];
        restRotations = new Quaternion[n];
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < n; i++)
        {
            if (cards[i] == null)
                continue;
            restPositions[i] = cards[i].anchoredPosition;
            restRotations[i] = cards[i].localRotation;
            sum += restPositions[i];
        }
        middle = n > 0 ? sum / n : Vector2.zero;
    }

    // The hand's arc: a circle whose top passes through the middle, with the
    // cards spread evenly either side and tilted to follow it. Worked out on
    // every play, so tuning it in Play mode shows at once.
    private void LayOutArc()
    {
        int n = cards.Length;

        // Tighten the spacing if the outermost cards would land too far apart
        float spacing = arcSpacing;
        if (maxFanWidth > 0f && n > 1 && arcRadius > 0f)
        {
            float halfWidth = Mathf.Min(maxFanWidth / 2f, arcRadius);
            float maxOuterAngle = Mathf.Asin(halfWidth / arcRadius) * Mathf.Rad2Deg;
            spacing = Mathf.Min(spacing, 2f * maxOuterAngle / (n - 1));
        }

        float start = -spacing * (n - 1) / 2f;
        for (int i = 0; i < n; i++)
        {
            float angle = start + i * spacing;
            float rad = angle * Mathf.Deg2Rad;
            restPositions[i] = middle + new Vector2(Mathf.Sin(rad) * arcRadius, (Mathf.Cos(rad) - 1f) * arcRadius);
            restRotations[i] = Quaternion.Euler(0f, 0f, -angle);
        }
    }

    protected override void Animate(Sequence sequence, TableEventContext context)
    {
        Remember();
        if (restPositions == null)
            return;
        if (useArc)
            LayOutArc();
        stackPoint = middle + stackOffset;

        int n = cards.Length;
        for (int i = 0; i < n; i++)
        {
            var card = cards[i];
            if (card == null)
                continue;

            // Stacked: all on one spot, each tilted a touch differently
            float tilt = n > 1 ? Mathf.Lerp(-stackTilt, stackTilt, i / (float)(n - 1)) : 0f;
            card.anchoredPosition = stackPoint;
            card.localRotation = Quaternion.Euler(0f, 0f, tilt);

            // The top card leaves first, so the fan opens from the front
            float at = fanDelay + (n - 1 - i) * stagger;
            sequence.Insert(at, card.DOAnchorPos(restPositions[i], fanSeconds).SetEase(fanEase));
            sequence.Insert(at, card.DOLocalRotateQuaternion(restRotations[i], fanSeconds).SetEase(fanEase));
        }

        float labelAt = fanDelay + (n - 1) * stagger + fanSeconds * 0.7f;
        if (label != null)
        {
            label.localScale = Vector3.zero;
            sequence.Insert(labelAt, label.DOScale(1f, labelPopSeconds).SetEase(Ease.OutBack));
        }
        if (labelText != null && !string.IsNullOrEmpty(labelFormat) && context.DrawTotal > 0)
            labelText.text = string.Format(labelFormat, context.DrawTotal);

        // How many Draw cards are stacked: the total owed over this card's own count
        int stacked = n > 0 ? context.DrawTotal / n : 0;
        if (multiplierText != null)
        {
            bool show = stacked >= 2;
            multiplierText.gameObject.SetActive(show);
            if (show)
            {
                var badge = multiplierText.rectTransform;
                multiplierText.text = string.Format(multiplierFormat, stacked);
                badge.localScale = Vector3.zero;
                float at = labelAt + labelPopSeconds * 0.5f;
                sequence.Insert(at, badge.DOScale(1f, labelPopSeconds).SetEase(Ease.OutBack));
                sequence.Insert(at + labelPopSeconds, badge.DOPunchScale(Vector3.one * multiplierPunch, 0.3f, 6, 0.5f));
            }
        }
    }
}
