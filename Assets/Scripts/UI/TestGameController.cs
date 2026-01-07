using UnityEngine;

public class TestGameController : MonoBehaviour
{
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private HandView handView;
    [SerializeField] private CardDatabase database;

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
            handView.RemoveCard(request.Card);
        }
        else if (request.ActionType == PlayerActionType.DrawCard)
        {
            var random = database.Cards[
                Random.Range(0, database.Cards.Count)];

            handView.AddCard(new CardInstance(random.Id));
        }
    }
}