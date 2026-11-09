using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Temporary: plays any table event panel on this screen only, to check how it
/// looks while a match runs. Nothing is sent to the server. Hidden in release
/// builds; delete it (and its dropdown and button) once the panels look right.
/// </summary>
public class TableEventsPreview : MonoBehaviour
{
    [SerializeField] private TableEventsView view;
    [SerializeField] private TMP_Dropdown eventDropdown;
    [SerializeField] private Button playButton;
    [Tooltip("For Draw Two / Four: how many are stacked, to see the x2 / x3 badge. 1 = not stacked.")]
    [Min(1)] [SerializeField] private int stack = 1;
    [Tooltip("For ColorChange: the colour to show.")]
    [SerializeField] private CardColor color = CardColor.Blue;

    private static readonly TableEvent[] Events = (TableEvent[])Enum.GetValues(typeof(TableEvent));

    private void Awake()
    {
        if (!Debug.isDebugBuild)
        {
            gameObject.SetActive(false);
            return;
        }

        if (eventDropdown != null)
        {
            eventDropdown.ClearOptions();
            eventDropdown.AddOptions(new List<string>(Array.ConvertAll(Events, e => e.ToString())));
        }
    }

    private void OnEnable()
    {
        if (playButton != null)
            playButton.onClick.AddListener(Play);
    }

    private void OnDisable()
    {
        if (playButton != null)
            playButton.onClick.RemoveListener(Play);
    }

    private void Play()
    {
        if (view == null || eventDropdown == null)
            return;
        var tableEvent = Events[Mathf.Clamp(eventDropdown.value, 0, Events.Length - 1)];
        int perCard = tableEvent == TableEvent.DrawTwo ? 2 : tableEvent == TableEvent.WildDrawFour ? 4 : 0;
        view.Show(tableEvent, 0f, perCard * stack, color);
    }
}
