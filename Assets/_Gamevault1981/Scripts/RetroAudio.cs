using System.Collections.Generic;
using UnityEngine;

public class RetroAudio : MonoBehaviour
{
    struct Beep { public float f; public float t; public float gain; }
    readonly Queue<Beep> q = new Queue<Beep>();
    float phase; float sr; Beep cur; bool has; float tleft;

    void Awake() { sr = AudioSettings.outputSampleRate; }

    public void BeepOnce(float freq, float sec, float gain=0.1f)
    { q.Enqueue(new Beep{ f=freq, t=sec, gain=gain }); }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!has && q.Count>0) { cur = q.Dequeue(); tleft = cur.t; has=true; }
        for (int i=0;i<data.Length;i+=channels)
        {
            float s=0f;
            if (has)
            {
                phase += cur.f/sr;
                if (phase>=1f) phase-=1f;
                s = (phase<0.5f?1f:-1f)*cur.gain;
                tleft -= 1f/sr;
                if (tleft<=0f) has=false;
            }
            for (int c=0;c<channels;c++) data[i+c]+=s;
        }
    }
}
