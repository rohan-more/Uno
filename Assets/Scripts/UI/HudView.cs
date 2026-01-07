using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HudView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private int playerIndex;
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
            ActionType = PlayerActionType.DrawCard
        });
    }
}
