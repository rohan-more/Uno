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

    private NakamaConnection Connection => NakamaConnection.Instance;

    private void OnEnable()
    {
        if (Connection != null)
            Connection.OnMatchState += HandleMatchState;
    }

    private void OnDisable()
    {
        if (Connection != null)
            Connection.OnMatchState -= HandleMatchState;
    }

    private void HandleMatchState(long opCode, string json)
    {
        if (opCode != UnoOpCodes.GameState)
            return;

        if (SceneManager.GetActiveScene().name == matchSceneName)
            return; // already there; this is just a resync

        SceneManager.LoadScene(matchSceneName);
    }

    /// <summary>Back to the home screen, e.g. after the results screen.</summary>
    public void ReturnHome()
    {
        SceneManager.LoadScene(homeSceneName);
    }
}
