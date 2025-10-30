using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class RetroAudio : MonoBehaviour
{
    public static float GlobalSfxVolume = 1f;

    AudioSource _src;
    const int sampleRate = 44100;

    void Awake()
    {
        _src = GetComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = false;
    }

    // Makes a short square beep at hz for seconds with optional volume
    public void BeepOnce(float hz, float seconds = 0.08f, float volume = 0.25f)
    {
        int len = Mathf.Max(1, Mathf.RoundToInt(seconds * sampleRate));
        var clip = AudioClip.Create("beep", len, 1, sampleRate, false);

        float phase = 0f;
        float step = Mathf.Max(1e-5f, hz) / sampleRate; // cycles per sample

        var data = new float[len];
        for (int i = 0; i < len; i++)
        {
            phase += step;
            if (phase >= 2f) phase -= 2f;

            // square wave
            data[i] = (phase < 1f ? 1f : -1f) * volume * GlobalSfxVolume;
        }

        clip.SetData(data, 0);
        _src.clip = clip;
        _src.volume = 1f; // already baked into data
        _src.Play();
        Destroy(clip, seconds + 0.1f);
    }
}