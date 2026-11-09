using UnityEngine;

/// <summary>
/// The track a scene plays: put one in each scene (e.g. Menu in Lobby, Match
/// in MatchScene). AudioService crossfades to it, and keeps it playing if it's
/// already on, so moving between scenes with the same track doesn't restart it.
/// </summary>
public class SceneMusic : MonoBehaviour
{
    [SerializeField] private Music music = Music.Menu;

    private void Start() => Services.Audio?.PlayMusic(music);
}
