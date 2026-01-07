using UnityEngine;

public class TestGameController : MonoBehaviour
{
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private HandView handView;
    [SerializeField] private CardDatabase database;
    [SerializeField] private CardProxyView cardProxy;
    [SerializeField] private RectTransform centerAnchor;
    [SerializeField] private RectTransform discardAnchor;
    
    private CardItem selectedCard;
    private void Awake()
    {
        actionBus.OnActionRequested += HandleAction;
    }

    private void OnDestroy()
    {
        actionBus.OnActionRequested -= HandleAction;
    }

    private void HandleAction(PlayerActionRequest request)
    {
        if (request.ActionType == PlayerActionType.PlayCard)
        {
            // Test behavior: discard card
            TryPlayCard(request);
            handView.RemoveCard(request.Card);
        }
        else if (request.ActionType == PlayerActionType.DrawCard)
        {
            var random = database.Cards[
                Random.Range(0, database.Cards.Count)];

            handView.AddCard(new CardInstance(random.Id));
        }
    }
    
    private void TryPlayCard(PlayerActionRequest request)
    {
        CardItem card = handView.GetCardItem(request.Card);

        if (selectedCard == null)
        {
            SelectCard(card);
        }
        else if (selectedCard == card)
        {
            ConfirmPlay(card);
        }
        else
        {
            Deselect();
            SelectCard(card);
        }
    }
    
    private void SelectCard(CardItem card)
    {
        RectTransform proxyParent = cardProxy.transform.parent as RectTransform;
        selectedCard = card;
        Vector2 startPos =
            RectTransformUtil.WorldToAnchored(
                card.RectTransform,
                proxyParent
            );
        
        Vector2 centerPos =
            RectTransformUtil.WorldToAnchored(
                centerAnchor,
                proxyParent
            );

        card.SetVisible(false);

        cardProxy.Show(card.Sprite, startPos);
        
        cardProxy.AnimateToAndVanish(
            centerPos,
            onComplete: () =>
            {
         
            }
        );
    }
    
    private void ConfirmPlay(CardItem card)
    {
        cardProxy.AnimateToDiscard(
            discardAnchor.anchoredPosition,
            onComplete: () =>
            {
                handView.RemoveCard(card.Instance);
                cardProxy.Hide();
                selectedCard = null;
            }
        );
    }
    
    private void Deselect()
    {
        if (selectedCard == null) return;

        selectedCard.SetVisible(true);
        cardProxy.Hide();
        selectedCard = null;
    }
    
    public static Vector2 WorldToAnchoredPosition(
        RectTransform target,
        RectTransform reference)
    {
        Vector2 screenPoint =
            RectTransformUtility.WorldToScreenPoint(null, target.position);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            reference,
            screenPoint,
            null,
            out Vector2 localPoint
        );

        return localPoint;
    }
}