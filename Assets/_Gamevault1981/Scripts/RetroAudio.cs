using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class RetroAudio : MonoBehaviour
{
    AudioSource _src;

    void Awake()
    {
        _src = GetComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = false;
        _src.spatialBlend = 0f;
        _src.volume = 1f;
    }

    // Simple square beep (freq Hz, duration sec, gain 0..1)
    public void BeepOnce(float freq, float duration = 0.08f, float gain = 0.07f)
    {
        int rate = 44100;
        int count = Mathf.Max(8, Mathf.RoundToInt(duration * rate));
        var clip = AudioClip.Create("beep", count, 1, rate, false);

        // square wave
        float step = (freq / rate) * 2f;
        float phase = 0f;
        var data = new float[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = (phase < 1f ? 1f : -1f) * gain;
            phase += step;
            if (phase >= 2f) phase -= 2f;
        }
        clip.SetData(data, 0);

        _src.Stop();
        _src.clip = clip;
        _src.Play();
        Destroy(clip, duration + 0.25f);
    }

    // Tiny melody looper. Returns the coroutine so callers can StopCoroutine on it.
    public Coroutine PlaySong(MonoBehaviour host, float[] notes, float noteSec = 0.10f, float gapSec = 0.02f, float gain = 0.06f, bool loop = true)
    {
        if (host == null || notes == null || notes.Length == 0) return null;
        return host.StartCoroutine(SongCo(notes, noteSec, gapSec, gain, loop));
    }

    IEnumerator SongCo(float[] notes, float noteSec, float gapSec, float gain, bool loop)
    {
        do
        {
            for (int i = 0; i < notes.Length; i++)
            {
                BeepOnce(notes[i], noteSec, gain);
                yield return new WaitForSeconds(noteSec + gapSec);
            }
            yield return new WaitForSeconds(0.15f);
        } while (loop);
    }
}
