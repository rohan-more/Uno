using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Popups for the current scene, by way of Services.Popups.</summary>
public interface IPopupService
{
    void Show(PopupType type, object context = null, Action onCompleted = null);
    void CloseActive();
}

/// <summary>
/// Shows one popup at a time from its prefab list. One per scene that needs
/// popups; it registers itself as Services.Popups and leaves with the scene.
/// </summary>
public class PopupManager : MonoBehaviour, IPopupService
{
    [SerializeField] private List<PopupView> popupPrefabs;

    private readonly Dictionary<PopupType, PopupView> prefabLookup = new();
    private PopupView activePopup;

    private void Awake()
    {
        // Services.Popups is an interface, so "!= null" can't see that the
        // manager from a previous scene load has been destroyed: ask Unity instead.
        if (Services.Popups is PopupManager existing && existing != null && existing != this)
        {
            Destroy(gameObject);
            return;
        }

        Services.Register(this);

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

        instance.Show(context, () =>
            {
                onCompleted?.Invoke();
                Destroy(instance.gameObject);
                activePopup = null;
            }
        );
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    public void CloseActive()
    {
        if (activePopup == null) return;

        PopupView popup = activePopup;
        activePopup = null;
        popup.Hide(); // Hide → Complete → destroy callback
    }
}