using System;
using UnityEngine;
using UnityEngine.UI;

public class CardItem : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image cardImage;
    [SerializeField] private Button button;
    private RectTransform rect;
    private CardInstance instance;
    private PlayerActionBus actionBus;
    private int playerIndex;
    
    public RectTransform RectTransform => rect;
    public CardInstance Instance => instance;
    public Sprite Sprite => cardImage.sprite;
    
    public CardInstance GetInstance() => instance;
    void Awake()
    {
        rect = GetComponent<RectTransform>();
    }

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
    
    public void SetVisible(bool visible)
    {
        cardImage.enabled = visible;
        button.enabled = visible;
    }

}