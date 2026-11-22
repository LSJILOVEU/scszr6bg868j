using System;
using UnityEngine;

namespace WarCore.Unity
{
    // SHOW-1: placeholder sound made in code (no audio files): a gunshot, a footstep, a body hit and the grenade preview's
    // burst. Loudness falls with the distance from the controlled unit, and at most MaxShotsPerTick gunshots start on one
    // tick so that a big fight does not turn into one noise. Compiled only when the project has the audio module
    // (WARCORE_AUDIO, set by the assembly definition); without it every call does nothing and Available is false.
    public sealed class ShowcaseSound
    {
        public const int MaxShotsPerTick = 4;
        public const float HearMetres = 70f;

#if WARCORE_AUDIO
        private const int Rate = 22050, Voices = 8;
        private readonly AudioSource[] voices = new AudioSource[Voices];
        private readonly AudioClip shot, step, thud, boom;
        private int next, shotsThisTick;

        public bool Available => true;
        public bool Muted { get; set; }

        public ShowcaseSound(GameObject host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null) host.AddComponent<AudioListener>();
            for (int i = 0; i < Voices; i++)
            {
                voices[i] = host.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
                voices[i].spatialBlend = 0f;
            }
            var noise = new System.Random(7);
            shot = Clip("Shot", 0.16f, noise, (t, n, low) => n * Mathf.Exp(-t * 42f) * 0.8f + Mathf.Sin(2f * Mathf.PI * (120f - 300f * t) * t) * Mathf.Exp(-t * 28f) * 0.6f);
            step = Clip("Step", 0.07f, noise, (t, n, low) => low * Mathf.Exp(-t * 60f) * 1.4f);
            thud = Clip("Thud", 0.18f, noise, (t, n, low) => Mathf.Sin(2f * Mathf.PI * 75f * t) * Mathf.Exp(-t * 22f) * 0.9f + low * Mathf.Exp(-t * 40f) * 0.5f);
            boom = Clip("Boom", 0.9f, noise, (t, n, low) => low * Mathf.Exp(-t * 5f) * 1.6f + Mathf.Sin(2f * Mathf.PI * (60f - 20f * t) * t) * Mathf.Exp(-t * 7f) * 0.8f);
        }

        // wave(seconds, white noise -1..1, the same noise smoothed) -> sample.
        private static AudioClip Clip(string name, float seconds, System.Random noise, Func<float, float, float, float> wave)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            float low = 0f;
            for (int i = 0; i < count; i++)
            {
                float n = (float)noise.NextDouble() * 2f - 1f;
                low += (n - low) * 0.12f;
                data[i] = Mathf.Clamp(wave((float)i / Rate, n, low * 3f), -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void Play(AudioClip clip, float volume, float pitch)
        {
            if (Muted || volume < 0.02f) return;
            AudioSource voice = voices[next = (next + 1) % Voices];
            voice.pitch = pitch;
            voice.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        // 1 beside the listener, fading to nothing at HearMetres.
        private static float Near(float metres)
        {
            float share = 1f - Mathf.Clamp01(metres / HearMetres);
            return share * share;
        }

        // Call once when the simulation moved on, before that tick's shots.
        public void NewTick() => shotsThisTick = 0;

        public void Shot(float metres, bool mine)
        {
            if (shotsThisTick >= MaxShotsPerTick && !mine) return;
            shotsThisTick++;
            Play(shot, mine ? 0.8f : 0.55f * Near(metres), UnityEngine.Random.Range(0.85f, 1.2f));
        }

        public void Step() => Play(step, 0.35f, UnityEngine.Random.Range(0.85f, 1.15f));
        public void Hurt() => Play(thud, 0.9f, UnityEngine.Random.Range(0.9f, 1.1f));
        public void Burst(float metres) => Play(boom, 0.25f + 0.75f * Near(metres), UnityEngine.Random.Range(0.9f, 1.05f));
#else
        public bool Available => false;
        public bool Muted { get; set; }
        public ShowcaseSound(GameObject host) { }
        public void NewTick() { }
        public void Shot(float metres, bool mine) { }
        public void Step() { }
        public void Hurt() { }
        public void Burst(float metres) { }
#endif
    }
}
