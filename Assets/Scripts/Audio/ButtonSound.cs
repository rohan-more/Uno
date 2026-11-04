using UnityEngine;
using UnityEngine.UI;

/// <summary>Plays a sound when this button is clicked: the click, unless you pick another.</summary>
[RequireComponent(typeof(Button))]
public class ButtonSound : MonoBehaviour
{
    [SerializeField] private Sfx sound = Sfx.ButtonClick;

    private Button button;

    private void Awake() => button = GetComponent<Button>();

    private void OnEnable() => button.onClick.AddListener(Play);

    private void OnDisable() => button.onClick.RemoveListener(Play);

    private void Play() => Services.Audio?.Play(sound);
}
