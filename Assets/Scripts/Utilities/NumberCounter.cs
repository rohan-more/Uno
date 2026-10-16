using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Makes a TMP text count up or down to a new number instead of jumping, e.g.
/// a coin balance after a claim or a payout. Plain C#: the view that owns the
/// text creates one and calls Tick from its Update.
///
///   counter = new NumberCounter(coinsText, CoinFormat.Short);
///   counter.CountTo(wallet.Coins);   // whenever the number changes
///   counter.Tick();                  // every frame
/// </summary>
public class NumberCounter
{
    private readonly TMP_Text label;
    private readonly Func<long, string> format;
    private readonly Color baseColor;

    private long shown;  // what the label says now
    private long from;   // where the current count started
    private long target; // the number it's counting to
    private float startedAt;
    private bool hasValue;

    /// <summary>How long a change takes to count through, in seconds.</summary>
    public float Seconds { get; set; } = 0.8f;

    /// <summary>Tint while counting up; alpha 0 (the default) keeps the text's colour.</summary>
    public Color GainColor { get; set; } = Color.clear;

    /// <summary>Tint while counting down; alpha 0 (the default) keeps the text's colour.</summary>
    public Color LossColor { get; set; } = Color.clear;

    public bool IsCounting => hasValue && shown != target;

    public NumberCounter(TMP_Text label, Func<long, string> format = null)
    {
        this.label = label;
        this.format = format ?? (n => n.ToString("N0"));
        baseColor = label != null ? label.color : Color.white;
    }

    /// <summary>Shows a number at once, without counting.</summary>
    public void Set(long value)
    {
        hasValue = true;
        shown = from = target = value;
        Show(value, baseColor);
    }

    /// <summary>
    /// Counts from what's shown to value. The first number ever given just
    /// appears, so a screen doesn't open by counting up from 0.
    /// </summary>
    public void CountTo(long value)
    {
        if (!hasValue)
        {
            Set(value);
            return;
        }
        if (value == target)
            return;

        from = shown;
        target = value;
        startedAt = Time.unscaledTime;
    }

    /// <summary>Moves the count along. Call every frame.</summary>
    public void Tick()
    {
        if (!IsCounting)
            return;

        float t = Seconds > 0f ? Mathf.Clamp01((Time.unscaledTime - startedAt) / Seconds) : 1f;
        if (t >= 1f)
        {
            Set(target);
            return;
        }

        float eased = 1f - Mathf.Pow(1f - t, 3f); // fast, then settling
        shown = from + (long)((target - from) * (double)eased);

        var tint = target > from ? GainColor : LossColor;
        Show(shown, tint.a > 0f ? Color.Lerp(tint, baseColor, eased) : baseColor);
    }

    private void Show(long value, Color color)
    {
        if (label == null)
            return;
        label.text = format(value);
        label.color = color;
    }
}
