using System;
using System.Threading.Tasks;
using SimplyLocalize;
using UnityEngine;
#if TEST_ADDRESSABLES
using UnityEngine.AddressableAssets;
#endif

public sealed class ChapterAudioProbe : MonoBehaviour
{
    public string channel, set;
#if TEST_ADDRESSABLES
    public string tableGuid;
#endif
    public GameObject first, second;
    public AudioSource source;

    private async void Start()
    {
        try
        {
            await Until(() => Localization.IsInitialized);
            var runtime = Localization.Runtime;
            AudioClip Current() => runtime.GetAsset<AudioClip>("Diary/Voice", channel, set);
            Check(Current() == null && Localization.Get("Diary/Subtitle") == "Hello", "Initial content");
            first.SetActive(true);
            first.SetActive(false);
            first.SetActive(true);
            await Until(() => source.clip != null);
            var loaded = source.clip;
            Check(loaded == Current() && loaded.name == "Narration" && loaded.loadState == AudioDataLoadState.Loaded, "Prepared chapter audio");
            await Localization.SetLanguageAsync("ru");
            Check(Localization.Get("Diary/Subtitle") == "Привет" && source.clip == loaded, "Independent subtitles");
            second.SetActive(true);
            // Acquire a barrier on the same channel so the second object's request has committed.
            var barrier = await runtime.AcquireSetAsync(channel, set);
            await barrier.ReleaseAsync();
            first.SetActive(false);
            Check(Current() == loaded && source.clip == null, "Second owner still needs the original clip");
#if TEST_ADDRESSABLES
            var external = Addressables.LoadAssetAsync<LocalizationAssetTable>(tableGuid);
            await Until(() => external.IsDone);
            Check(external.Result.Get<AudioClip>("Diary/Voice") == loaded, "External owner must share the clip");
#endif
            second.SetActive(false);
            await Until(() => Current() == null);
#if TEST_ADDRESSABLES
            Check(loaded != null, "Localization released another Addressables owner's clip");
            Addressables.Release(external);
            await Until(() => loaded == null);
#endif
            first.SetActive(true);
            await Until(() => source.clip != null);
            Check(source.clip.name == "Narration" && Localization.Get("Diary/Subtitle") == "Привет", "Chapter re-entry");
            Debug.Log("PASS: chapter audio lifecycle");
            Application.Quit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            Application.Quit(1);
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + 45;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Chapter audio condition timed out");
            await Task.Yield();
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
