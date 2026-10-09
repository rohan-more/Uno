using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What the confirm popup asks, and who hears the answer.</summary>
public class ConfirmChoice : PopupCountdown
{
    public string Message;
    public string ConfirmLabel; // optional, e.g. "Leave"
    public Action OnConfirm;
    public Action OnCancel;
}

/// <summary>
/// A yes/no question, e.g. "Leave the match?". Opened through PopupManager as
/// PopupType.ConfirmAction with a ConfirmChoice context.
/// </summary>
public class ConfirmPopup : PopupView
{
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button confirmButton;
    [Tooltip("Optional: shows ConfirmLabel if one is given.")]
    [SerializeField] private TMP_Text confirmLabel;
    [SerializeField] private Button cancelButton;

    private ConfirmChoice choice;

    public override void Show(object context = null, Action onCompleted = null)
    {
        base.Show(context, onCompleted);

        choice = context as ConfirmChoice;
        if (choice == null)
            return;

        if (messageText != null)
            messageText.text = choice.Message;
        if (confirmLabel != null && !string.IsNullOrEmpty(choice.ConfirmLabel))
            confirmLabel.text = choice.ConfirmLabel;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        confirmButton.onClick.AddListener(ConfirmPressed);
        cancelButton.onClick.AddListener(CancelPressed);
    }

    private void OnDisable()
    {
        confirmButton.onClick.RemoveListener(ConfirmPressed);
        cancelButton.onClick.RemoveListener(CancelPressed);
    }

    private void ConfirmPressed() => Answer(choice?.OnConfirm);

    private void CancelPressed() => Answer(choice?.OnCancel);

    // Close first, so an answer that opens another popup isn't closed with this one
    private void Answer(Action answer)
    {
        choice = null;
        PopupManager.Instance.CloseActive();
        answer?.Invoke();
    }
}
