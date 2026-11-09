using DG.Tweening;

/// <summary>
/// A table event panel that just pops in and fades out, e.g. "Block!" growing
/// from nothing (Start Scale 0) or "Pickup 2" with a gentle pop (0.6).
/// </summary>
public class PopEventPanel : TableEventPanel
{
    protected override void Animate(Sequence sequence, TableEventContext context) { }
}
