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
        button.onClick.AddListener(OnDrawCardClicked);
        testButton.onClick.AddListener(ShowPopup);
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(OnDrawCardClicked);
        testButton.onClick.RemoveListener(ShowPopup);
    }

    private void ShowPopup()
    {
        PopupManager.Instance.Show(PopupType.ChooseColor);
    }

    private void OnDrawCardClicked()
    {
        // To-Do
        /*actionBus.RaiseCardDraw(new CardDrawEvent
        {
            PlayerIndex = playerIndex,
            Card = instance
        });*/
    }
}
