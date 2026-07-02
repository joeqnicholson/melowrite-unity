using System;
using UnityEngine;
using Melowrite;
using Melowrite.Audio;

// The Unity audio host for Melowrite. Auto-created at startup; you never place it. It points the
// shared MeloDirector at Unity (path resolution, logging, device sample rate) and pumps it:
// FillBuffer on the audio thread (OnAudioFilterRead), Tick on the main thread (Update).
[AddComponentMenu("")]
public sealed class MeloUnityHost : MonoBehaviour
{
    private float[] _stereo = Array.Empty<float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        MeloDirector.ResolvePath = MeloContent.Resolve;
        MeloDirector.LogError    = m => Debug.LogError(m);
        MeloDirector.LogWarning  = m => Debug.LogWarning(m);
        int rate = AudioSettings.outputSampleRate;
        MeloDirector.Init(rate > 0 ? rate : 44100);
        _ = MeloDirector.Instance;   // create on the main thread, not the audio thread
        Debug.Log($"[Melo] Melowrite Audio Engine build {MeloDirector.Build}");

        var go = new GameObject("[Melo]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<AudioSource>();       // OnAudioFilterRead needs an AudioSource present
        go.AddComponent<MeloUnityHost>();
    }

    void Update() => MeloDirector.Instance.Tick();

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (channels <= 0) { Array.Clear(data, 0, data.Length); return; }
        int frames = data.Length / channels;

        if (channels == 2)
        {
            MeloDirector.Instance.FillBuffer(data, frames);   // data is already stereo interleaved
            return;
        }

        int stereoLen = frames * 2;
        if (_stereo.Length < stereoLen) _stereo = new float[stereoLen];
        MeloDirector.Instance.FillBuffer(_stereo, frames);
        for (int f = 0; f < frames; f++)
        {
            float l = _stereo[f * 2], r = _stereo[f * 2 + 1];
            int dst = f * channels;
            if (channels == 1) { data[f] = (l + r) * 0.5f; continue; }
            data[dst] = l; data[dst + 1] = r;
            for (int c = 2; c < channels; c++) data[dst + c] = 0f;
        }
    }

    void OnDestroy() => MeloDirector.Instance.Shutdown();
}
