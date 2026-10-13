using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The loading screen. Runs every startup step in order (art, sign-in, coins)
/// and fills the bar by each step's share of the work, then opens the home
/// screen. If a step fails it says which, and Retry carries on from there.
///
/// Lives in the Startup scene, which is first in the build and loads once; the
/// objects that last the whole session (Nakama, the debug menu) live there too.
/// To add a step (e.g. player progress), add a line to BuildSteps.
/// </summary>
public class StartupLoader : MonoBehaviour
{
    public const string StartupSceneName = "Startup";

    [Header("UI")]
    [Tooltip("A Filled Image (Horizontal) for the bar.")]
    [SerializeField] private Image progressFill;
    [Tooltip("Optional: the current step, e.g. \"Signing in…\", or what went wrong.")]
    [SerializeField] private TMP_Text statusText;
    [Tooltip("Optional: shown when a step fails. Start it inactive.")]
    [SerializeField] private Button retryButton;

    [Header("Flow")]
    [SerializeField] private string homeSceneName = "Boot";
    [Tooltip("Keep the screen up at least this long, so a fast start doesn't flash.")]
    [SerializeField] private float minimumSeconds = 1.5f;
    [Tooltip("How quickly the bar catches up with the real progress.")]
    [SerializeField] private float fillSpeed = 1.5f;

    private class Step
    {
        public string Label;
        public float Weight;
        public Func<IProgress<float>, Task<bool>> Run;
    }

    private List<Step> steps;
    private int current;
    private float stepProgress; // 0-1 within the current step
    private float shownFill;
    private float startedAt;
    private bool running;

    private static NakamaConnection Connection => NakamaConnection.Instance;

    private void Start()
    {
        startedAt = Time.unscaledTime;
        if (retryButton != null)
        {
            retryButton.onClick.AddListener(Retry);
            retryButton.gameObject.SetActive(false);
        }

        steps = BuildSteps();
        RunFrom(0);
    }

    private List<Step> BuildSteps() => new()
    {
        new Step
        {
            Label = "Loading cards…", Weight = 0.30f,
            Run = async progress =>
            {
                await GameAssets.PreloadAsync(progress);
                return GameAssets.Loaded;
            },
        },
        new Step
        {
            Label = "Signing in…", Weight = 0.45f,
            Run = async _ => Connection != null && (Connection.IsConnected || await Connection.ConnectAsync()),
        },
        new Step
        {
            Label = "Loading your coins…", Weight = 0.25f,
            Run = async _ => await Connection.Wallet.RefreshAsync(),
        },
    };

    private async void RunFrom(int first)
    {
        running = true;
        for (current = first; current < steps.Count; current++)
        {
            var step = steps[current];
            stepProgress = 0f;
            SetStatus(step.Label);

            bool ok;
            try
            {
                ok = await step.Run(new Progress<float>(p => stepProgress = Mathf.Clamp01(p)));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }

            if (!ok)
            {
                Fail(step);
                return;
            }
            stepProgress = 1f;
        }

        // Let the bar reach the end, and don't flash past a quick start
        while (shownFill < 0.999f || Time.unscaledTime - startedAt < minimumSeconds)
            await Task.Yield();

        running = false;
        SceneManager.LoadScene(homeSceneName);
    }

    private void Fail(Step step)
    {
        running = false;
        Debug.LogWarning($"Startup step failed: {step.Label}");
        SetStatus(step == steps[0]
            ? "Couldn't load the game's art. Try again?"
            : "Couldn't reach the server. Check your connection and try again.");
        if (retryButton != null)
            retryButton.gameObject.SetActive(true);
    }

    private void Retry()
    {
        if (running)
            return;
        if (retryButton != null)
            retryButton.gameObject.SetActive(false);
        RunFrom(current);
    }

    private void Update()
    {
        if (progressFill == null || steps == null)
            return;

        // Done steps count fully, the current one by how far it's got
        float target = 0f;
        for (int i = 0; i < steps.Count; i++)
        {
            if (i < current)
                target += steps[i].Weight;
            else if (i == current)
                target += steps[i].Weight * stepProgress;
        }
        target = Mathf.Clamp01(target / TotalWeight());

        shownFill = Mathf.MoveTowards(shownFill, target, fillSpeed * Time.unscaledDeltaTime);
        progressFill.fillAmount = shownFill;
    }

    private float TotalWeight()
    {
        float total = 0f;
        foreach (var step in steps)
            total += step.Weight;
        return total > 0f ? total : 1f;
    }

    private void SetStatus(string text)
    {
        if (statusText != null)
            statusText.text = text;
    }

#if UNITY_EDITOR
    // Pressing Play in another scene: go through Startup first, so the
    // connection and art are ready. Nothing in the session works without it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartFromStartupInEditor()
    {
        if (SceneManager.GetActiveScene().name != StartupSceneName && NakamaConnection.Instance == null)
            SceneManager.LoadScene(StartupSceneName);
    }
#endif
}
