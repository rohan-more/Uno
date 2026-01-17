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

public struct CardClickedEvent
{
    public int PlayerIndex;
    public CardInstance Card;
}

public struct CardDrawEvent
{
    public int PlayerIndex;
    public CardInstance Card;
}


public enum PlayerSeat
{
    BottomPlayer,
    BottomLeftPlayer,
    BottomRightPlayer,
    TopPlayer,
    TopLeftPlayer,
    TopRightPlayer,
    RightPlayer,
    Deck
}

public class PlayerActionBus : MonoBehaviour
{
    public event Action<CardClickedEvent> OnCardClicked;
    
    public event Action<CardDrawEvent> OnCardDraw;
    public event Action<PlayerActionRequest> OnActionRequested;

    public void RaiseCardClicked(CardClickedEvent evt) => OnCardClicked?.Invoke(evt);

    public void RaiseAction(PlayerActionRequest req) => OnActionRequested?.Invoke(req);

    public void RaiseCardDraw(CardDrawEvent obj) => OnCardDraw?.Invoke(obj);

}
