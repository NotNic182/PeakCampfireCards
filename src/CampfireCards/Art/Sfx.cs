using System;
using System.Collections.Generic;
using CampfireCards.Generated;
using UnityEngine;

namespace CampfireCards.Art
{
    /// <summary>systems.json Sfx: synthesises the sounds.json UI sounds into AudioClips (no audio files) and plays them.</summary>
    public static class Sfx
    {
        private const int Rate = 44100;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, float> Volumes = new Dictionary<string, float>();
        private static AudioSource _source;

        public static void Play(string id, float volume = 1f)
        {
            if (string.IsNullOrEmpty(id) || id == "none") return;
            if (Plugin.SoundsEnabled != null && !Plugin.SoundsEnabled.Value) return;
            AudioClip clip;
            if (!Clips.TryGetValue(id, out clip))
            {
                clip = Make(id);
                Clips[id] = clip;
            }
            if (clip == null) return;
            Source.PlayOneShot(clip, volume * Volumes[id]);
        }

        private static AudioSource Source
        {
            get
            {
                if (_source != null) return _source;
                var go = new GameObject("CampfireCardsAudio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _source = go.AddComponent<AudioSource>();
                _source.spatialBlend = 0f;
                _source.playOnAwake = false;
                _source.ignoreListenerPause = true;
                return _source;
            }
        }

        private static AudioClip Make(string id)
        {
            foreach (var d in SoundTable.Sounds)
            {
                if (d.Id != id) continue;
                float[] pcm;
                switch (d.Kind)
                {
                    case "tick": pcm = Tick(d); break;
                    case "chime": pcm = Chime(d); break;
                    case "whoosh": pcm = Whoosh(d); break;
                    default: return null;
                }
                Normalise(pcm, 0.9f);
                var clip = AudioClip.Create("CampfireCards/" + id, pcm.Length, 1, Rate, false);
                clip.SetData(pcm, 0);
                Volumes[id] = d.Volume;
                return clip;
            }
            return null;
        }

        private static float[] Tick(SoundDef d)
        {
            int n = (int)(d.Duration * Rate);
            var pcm = new float[n];
            float f = d.Freqs[0];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                pcm[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 90f);
            }
            return pcm;
        }

        private static float[] Chime(SoundDef d)
        {
            int n = (int)(d.Duration * Rate);
            var pcm = new float[n];
            for (int k = 0; k < d.Freqs.Length; k++)
            {
                float start = k * 0.085f, f = d.Freqs[k];
                for (int i = (int)(start * Rate); i < n; i++)
                {
                    float t = (float)i / Rate - start;
                    float env = Mathf.Exp(-t * 5.5f) * Mathf.Clamp01(t * 400f);
                    pcm[i] += (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t) + 0.12f * Mathf.Sin(6.03f * Mathf.PI * f * t)) * env;
                }
            }
            return pcm;
        }

        private static float[] Whoosh(SoundDef d)
        {
            int n = (int)(d.Duration * Rate);
            var pcm = new float[n];
            var rng = new System.Random(1234);
            float low = 0f, band = 0f;
            for (int i = 0; i < n; i++)
            {
                float p = (float)i / n;
                float cutoff = Mathf.Lerp(d.Freqs[0], d.Freqs[1], p);
                float fc = 2f * Mathf.Sin(Mathf.PI * cutoff / Rate);
                const float q = 0.6f;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // state-variable band-pass filter sweeping upward
                low += fc * band;
                float high = noise - low - q * band;
                band += fc * high;
                pcm[i] = band * Mathf.Sin(Mathf.PI * p);
            }
            return pcm;
        }

        private static void Normalise(float[] pcm, float peak)
        {
            float max = 0f;
            foreach (var v in pcm) max = Math.Max(max, Math.Abs(v));
            if (max <= 0f) return;
            float k = peak / max;
            for (int i = 0; i < pcm.Length; i++) pcm[i] *= k;
        }
    }
}
