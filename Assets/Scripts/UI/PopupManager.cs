using System;
using System.Collections.Generic;
using UnityEngine;

public interface IPopupService
{
    void Show(PopupType type, object context = null, Action onCompleted = null);
    void CloseActive();
}
public class PopupManager : MonoBehaviour, IPopupService
{
    public static IPopupService Instance { get; private set; }

    [SerializeField] private List<PopupView> popupPrefabs;

    private readonly Dictionary<PopupType, PopupView> prefabLookup = new();
    private PopupView activePopup;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        foreach (var prefab in popupPrefabs)
        {
            if (prefabLookup.ContainsKey(prefab.Type))
            {
                Debug.LogError($"Duplicate popup prefab for type {prefab.Type}");
                continue;
            }

            prefabLookup.Add(prefab.Type, prefab);
        }
    }

    public void Show(PopupType type, object context = null, Action onCompleted = null)
    {
        if (!prefabLookup.TryGetValue(type, out var prefab))
        {
            Debug.LogError($"Popup prefab not registered: {type}");
            return;
        }

        CloseActive();

        PopupView instance = Instantiate(prefab, transform);
        activePopup = instance;

        instance.Show(
            context,
            () =>
            {
                onCompleted?.Invoke();
                Destroy(instance.gameObject);
                activePopup = null;
            }
        );
    }

    public void CloseActive()
    {
        if (activePopup == null) return;

        PopupView popup = activePopup;
        activePopup = null;
        popup.Hide(); // Hide → Complete → destroy callback
    }
}