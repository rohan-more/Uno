using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum PlayerActionType
{
    PlayCard,
    DrawCard,
    ChooseColor
}

public struct PlayerActionRequest
{
    public int PlayerIndex;
    public PlayerActionType ActionType;
    public CardInstance Card;   // optional
    public CardColor ChosenColor; // optional
}
public class PlayerActionBus : MonoBehaviour
{
    public event Action<PlayerActionRequest> OnActionRequested;

    public void Raise(PlayerActionRequest request)
    {
        OnActionRequested?.Invoke(request);
    }
}
