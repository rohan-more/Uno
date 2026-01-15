using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HudView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Button testButton;
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private int playerIndex;
    void OnEnable()
    {
        button.onClick.AddListener(OnClicked);
        testButton.onClick.AddListener(ShowPopup);
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(OnClicked);
        testButton.onClick.RemoveListener(ShowPopup);
    }

    private void ShowPopup()
    {
        PopupManager.Instance.Show(PopupType.ChooseColor);
    }

    private void OnClicked()
    {
        actionBus.Raise(new PlayerActionRequest
        {
            PlayerIndex = playerIndex,
            ActionType = PlayerActionType.DrawCard
        });
    }
}
