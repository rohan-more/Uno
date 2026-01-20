using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ChooseWildColorView : MonoBehaviour
{
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private Button redButton;
    [SerializeField] private Button blueButton;
    [SerializeField] private Button yellowButton;
    [SerializeField] private Button greenButton;

    
    private void OnEnable()
    {
        actionBus = FindObjectOfType<PlayerActionBus>();
        
        redButton.onClick.AddListener((() =>
        {
            actionBus.RaiseCardColor(CardColor.Red);
            PopupManager.Instance.CloseActive();
        }));
        
        blueButton.onClick.AddListener((() =>
        {
            actionBus.RaiseCardColor(CardColor.Blue);
            PopupManager.Instance.CloseActive();
        }));
        
        yellowButton.onClick.AddListener((() =>
        {
            actionBus.RaiseCardColor(CardColor.Yellow);
            PopupManager.Instance.CloseActive();
        }));
        
        greenButton.onClick.AddListener((() =>
        {
            actionBus.RaiseCardColor(CardColor.Green);
            PopupManager.Instance.CloseActive();
        }));
    }

    private void OnDisable()
    {
        redButton.onClick.RemoveAllListeners();
        blueButton.onClick.RemoveAllListeners();
        yellowButton.onClick.RemoveAllListeners();
        greenButton.onClick.RemoveAllListeners();
    }
}
