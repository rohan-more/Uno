using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Watches for the server starting a match and opens the match scene.
///
/// Put this on the same persistent object as NakamaConnection: the first
/// GAME_STATE arrives while the home screen is still showing, and the presenter
/// in the match scene asks for a fresh snapshot once it loads.
/// </summary>
public class MatchSceneLoader : MonoBehaviour
{
    [SerializeField] private string matchSceneName = "MatchScene";
    [SerializeField] private string homeSceneName = "Boot";

    [Tooltip("Open the table this long before the first turn, so it appears just as play starts.")]
    [SerializeField] private float sceneLoadAllowance = 0.5f;

    private bool loading;

    private static IConnectionService Connection => Services.Connection;

    // Start, not OnEnable: by then every Awake has run, so NakamaConnection exists
    private void Start()
    {
        if (Connection != null)
            Connection.OnMatchState += HandleMatchState;
        else
            Debug.LogError("MatchSceneLoader found no NakamaConnection: it won't hear the deal, " +
                           "so the match scene will never open.");
    }

    private void OnDestroy()
    {
        if (Connection != null)
            Connection.OnMatchState -= HandleMatchState;
    }

    private void HandleMatchState(long opCode, string json)
    {
        if (opCode != UnoOpCodes.GameState)
            return;

        if (SceneManager.GetActiveScene().name == matchSceneName || loading)
            return; // already there or on the way; this is just a resync

        // The server pauses before the first turn so everyone sees the full
        // table of players: spend that pause on the matchmaking screen, not at
        // the table. Rejoining mid-match has no pause and goes straight in.
        var state = JsonUtility.FromJson<GameStateMsg>(json);
        float wait = Mathf.Max(0f, state != null ? state.startsInMs / 1000f - sceneLoadAllowance : 0f);
        Debug.Log($"[SCENE] Deal received; opening {matchSceneName} in {wait:0.0} s");
        StartCoroutine(LoadMatchScene(wait));
    }

    private IEnumerator LoadMatchScene(float wait)
    {
        loading = true;
        if (wait > 0f)
            yield return new WaitForSeconds(wait);

        Debug.Log($"[SCENE] Opening {matchSceneName}");
        SceneManager.LoadScene(matchSceneName);
        loading = false;
    }

    /// <summary>Back to the home screen, e.g. after the results screen.</summary>
    public void ReturnHome()
    {
        SceneManager.LoadScene(homeSceneName);
    }
}
