using System.Collections.Generic;
using PeakRounds.Generated;
using UnityEngine;

namespace PeakRounds.Rounds
{
    /// <summary>Plays ROUNDS's own UI sounds (rounds_assets.json audioKeys) through one 2D AudioSource.</summary>
    public static class RoundsAudio
    {
        private static AudioSource _source;
        private static readonly System.Random Rng = new System.Random();

        private static AudioSource Source
        {
            get
            {
                if (_source != null) return _source;
                var go = new GameObject("PeakRoundsAudio");
                Object.DontDestroyOnLoad(go);
                _source = go.AddComponent<AudioSource>();
                _source.spatialBlend = 0f;
                _source.playOnAwake = false;
                _source.ignoreListenerPause = true;
                _source.volume = 0.7f;
                return _source;
            }
        }

        private static string[] NamesFor(string key)
        {
            switch (key)
            {
                case "cardsAppear": return RoundsAudioKeys.CardsAppear;
                case "cardSwitch": return RoundsAudioKeys.CardSwitch;
                case "cardPick": return RoundsAudioKeys.CardPick;
                case "hover": return RoundsAudioKeys.Hover;
                default: return null;
            }
        }

        /// <summary>Plays a random variant of the key; silent when ROUNDS is missing, the clip failed to decode or sounds are off.</summary>
        public static void Play(string key, float volume = 1f)
        {
            if (key == null || key == "none") return;
            if (Plugin.SoundsEnabled != null && !Plugin.SoundsEnabled.Value) return;
            var data = RoundsReader.Data;
            if (data == null) return;
            var names = NamesFor(key);
            if (names == null || names.Length == 0) return;
            var candidates = new List<AudioClip>();
            foreach (var n in names)
            {
                RoundsClip c;
                if (data.Clips.TryGetValue(n, out c) && c.Pcm != null)
                {
                    var clip = c.Clip;
                    if (clip != null) candidates.Add(clip);
                }
            }
            if (candidates.Count == 0) return;
            Source.PlayOneShot(candidates[Rng.Next(candidates.Count)], volume);
        }
    }
}
