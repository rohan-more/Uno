using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Switches between the home screen and the matchmaking panel by fading their
/// CanvasGroups. Works with no server: if there's no connection yet it shows a
/// placeholder name and avatar, so the flow can be tested on its own.
/// </summary>
public class ScreenFlow : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private CanvasGroup lobbyPanel;
    [SerializeField] private CanvasGroup matchmakingPanel;

    [Header("Buttons")]
    [SerializeField] private Button findMatchButton;
    [SerializeField] private Button matchmakingExitButton;

    [Header("Matchmaking")]
    [SerializeField] private MatchmakingPanel matchmaking;

    [Header("Fade")]
    [SerializeField] private float fadeSeconds = 0.15f;

    [Header("Offline testing")]
    [SerializeField] private string fallbackName = "You";
    [SerializeField] private int fallbackAvatarIndex = 0;

    private Coroutine _fade;

    private NakamaConnection Connection => NakamaConnection.Instance;

    private void OnEnable()
    {
        if (findMatchButton != null)
            findMatchButton.onClick.AddListener(ShowMatchmaking);

        if (matchmakingExitButton != null)
            matchmakingExitButton.onClick.AddListener(ShowLobby);
    }

    private void OnDisable()
    {
        if (findMatchButton != null)
            findMatchButton.onClick.RemoveListener(ShowMatchmaking);

        if (matchmakingExitButton != null)
            matchmakingExitButton.onClick.RemoveListener(ShowLobby);
    }

    private void Start()
    {
        SetVisible(lobbyPanel, true, instant: true);
        SetVisible(matchmakingPanel, false, instant: true);
    }

    /// <summary>
    /// Home screen visible, matchmaking hidden. Backing out of matchmaking also
    /// leaves the match, or the server would keep the seat and start the game
    /// without you.
    /// </summary>
    public async void ShowLobby()
    {
        if (matchmaking != null)
            matchmaking.ClearSlots();

        Switch(from: matchmakingPanel, to: lobbyPanel);

        if (Connection != null)
            await Connection.LeaveMatchAsync();
    }

    /// <summary>
    /// Matchmaking visible with your seat already filled and the other three
    /// scrolling. The actual server search is hooked up separately.
    /// </summary>
    public void ShowMatchmaking()
    {
        if (matchmaking != null)
        {
            var connected = Connection != null && Connection.IsConnected;
            var playerName = connected ? Connection.DisplayName : fallbackName;
            var avatar = connected ? Connection.AvatarIndex : fallbackAvatarIndex;
            matchmaking.BeginSearch(playerName, avatar);
        }

        Switch(from: lobbyPanel, to: matchmakingPanel);
    }

    private void Switch(CanvasGroup from, CanvasGroup to)
    {
        if (_fade != null)
            StopCoroutine(_fade);

        SetVisible(from, false, instant: true);
        _fade = StartCoroutine(FadeIn(to));
    }

    private IEnumerator FadeIn(CanvasGroup group)
    {
        if (group == null)
            yield break;

        group.gameObject.SetActive(true);
        group.alpha = 0f;

        for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
        {
            group.alpha = Mathf.Clamp01(t / fadeSeconds);
            yield return null;
        }

        SetVisible(group, true, instant: true);
        _fade = null;
    }

    /// <summary>
    /// Alpha alone still leaves a panel clickable, so interactable and
    /// blocksRaycasts have to move with it.
    /// </summary>
    private void SetVisible(CanvasGroup group, bool visible, bool instant)
    {
        if (group == null)
            return;

        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;

        if (instant)
            group.gameObject.SetActive(visible);
    }
}
