using DG.Tweening;
using UnityEngine;

/// <summary>
/// The Reverse panel: pops in like any table event panel, and spins its arrows
/// in the new direction of play, so everyone sees the flow turn around.
/// </summary>
public class ReverseEventPanel : TableEventPanel
{
    [Tooltip("The arrows image that spins. Draw it pointing anticlockwise; it's mirrored for clockwise.")]
    [SerializeField] private RectTransform arrows;
    [Tooltip("How far it turns while the panel is up.")]
    [SerializeField] private float turnDegrees = 360f;
    [Tooltip("Tick if the arrows sprite points clockwise instead.")]
    [SerializeField] private bool spritePointsClockwise;

    protected override void Animate(Sequence sequence, TableEventContext context)
    {
        if (arrows == null)
            return;

        bool clockwise = context.Direction >= 0;

        // Mirror the sprite so its arrowheads lead the way it turns
        var scale = arrows.localScale;
        bool mirror = clockwise != spritePointsClockwise;
        arrows.localScale = new Vector3(Mathf.Abs(scale.x) * (mirror ? -1f : 1f), scale.y, scale.z);

        // Clockwise on screen is a negative z rotation in Unity
        arrows.localRotation = Quaternion.identity;
        float turn = clockwise ? -turnDegrees : turnDegrees;
        sequence.Insert(0f, arrows
            .DOLocalRotate(new Vector3(0f, 0f, turn), Duration, RotateMode.FastBeyond360)
            .SetEase(Ease.OutCubic));
    }
}
