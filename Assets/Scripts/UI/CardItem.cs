using System;
using UnityEngine;
using UnityEngine.UI;

public class CardItem : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image cardImage;
    [SerializeField] private Button button;

    private CardInstance instance;
    private PlayerActionBus actionBus;
    private int playerIndex;

    public void Bind(CardInstance instance, Sprite sprite, PlayerActionBus bus, int playerIndex)
    {
        this.instance = instance;
        this.actionBus = bus;
        this.playerIndex = playerIndex;

        cardImage.sprite = sprite;
    }

    public void SetInteractable(bool value)
    {
        button.interactable = value;
    }

    void OnEnable()
    {
        button.onClick.AddListener(OnClicked);
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(OnClicked);
    }

    private void OnClicked()
    {
        actionBus.Raise(new PlayerActionRequest
        {
            PlayerIndex = playerIndex,
            ActionType = PlayerActionType.PlayCard,
            Card = instance
        });
    }

    public CardInstance GetInstance() => instance;
}