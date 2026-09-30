using System.Collections.Generic;
using UnityEngine;

/// <summary>Arranges a hand's cards. HandView calls Layout whenever the hand changes.</summary>
public abstract class HandLayout : MonoBehaviour
{
    public abstract void Layout(List<RectTransform> cards);
}
