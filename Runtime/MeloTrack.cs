using System;
using System.Collections.Generic;
using Melowrite.Audio.Effects;
using Melowrite.Core;

namespace Melowrite.Audio
{
    /// <summary>
    /// Typed handle to one track in a loaded project. Cheap to hold or re-fetch.
    /// Returned by MeloInstance.Track(...).
    /// </summary>
    public readonly struct MeloTrack
    {
        private readonly Track _track;

        /// <summary>
        /// 0-based index in the project
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// false if the index didn't resolve to a real track
        /// </summary>
        public bool IsValid => _track != null;

        internal MeloTrack(Track track, int index) { _track = track; Index = index; }

        public string Name => _track?.Name ?? "";

        /// <summary>
        /// 0-1
        /// </summary>
        public float Volume
        {
            get => _track?.Volume ?? 0f;
            set { if (_track != null) _track.Volume = Math.Clamp(value, 0f, 1f); }
        }

        /// <summary>
        /// -1 left, 0 center, +1 right
        /// </summary>
        public float Pan
        {
            get => _track?.Pan ?? 0f;
            set { if (_track != null) _track.Pan = Math.Clamp(value, -1f, 1f); }
        }

        public bool Muted
        {
            get => _track?.Muted ?? false;
            set { if (_track != null) _track.Muted = value; }
        }

        public bool Solo
        {
            get => _track?.Solo ?? false;
            set { if (_track != null) _track.Solo = value; }
        }

        /// <summary>
        /// send to bus A (usually reverb), 0-1
        /// </summary>
        public float SendA
        {
            get => _track?.SendA ?? 0f;
            set { if (_track != null) _track.SendA = Math.Clamp(value, 0f, 1f); }
        }

        /// <summary>
        /// send to bus B (usually delay), 0-1
        /// </summary>
        public float SendB
        {
            get => _track?.SendB ?? 0f;
            set { if (_track != null) _track.SendB = Math.Clamp(value, 0f, 1f); }
        }

        /// <summary>
        /// swap the track's instrument; null clears it
        /// </summary>
        public void SetInstrument(Melowrite.Audio.Instruments.IInstrument? instrument)
        {
            if (_track != null) _track.Instrument = instrument;
        }

        public Melowrite.Audio.Instruments.IInstrument? Instrument => _track?.Instrument;

        // -- Effects --

        public int EffectCount => _track?.Effects.Count ?? 0;

        public IEnumerable<MeloEffect> Effects
        {
            get
            {
                if (_track == null) yield break;
                foreach (var fx in _track.Effects) yield return MeloEffect.Wrap(fx);
            }
        }

        public bool TryGetEffect<T>(out T effect) where T : MeloEffect
        {
            effect = GetEffect<T>();
            return effect != null;
        }

        public T? GetEffect<T>() where T : MeloEffect
        {
            if (_track == null) return null;
            foreach (var fx in _track.Effects)
            {
                var wrapped = MeloEffect.Wrap(fx);
                if (wrapped is T match) return match;
            }
            return null;
        }

        public bool HasEffect<T>() where T : MeloEffect => GetEffect<T>() != null;

        /// <summary>
        /// add an effect of type T to the end of the chain
        /// </summary>
        public T AddEffect<T>() where T : MeloEffect
        {
            if (_track == null) throw new InvalidOperationException("Track is invalid.");
            var raw = CreateUnderlyingFor<T>();
            _track.Effects.Add(raw);
            return (T)MeloEffect.Wrap(raw);
        }

        /// <summary>
        /// remove the first effect of type T; true if one was removed
        /// </summary>
        public bool RemoveEffect<T>() where T : MeloEffect
        {
            if (_track == null) return false;
            var concrete = UnderlyingTypeFor<T>();
            for (int i = 0; i < _track.Effects.Count; i++)
                if (concrete.IsInstanceOfType(_track.Effects[i]))
                {
                    _track.Effects.RemoveAt(i);
                    return true;
                }
            return false;
        }

        private static IEffect CreateUnderlyingFor<T>() where T : MeloEffect
        {
            var t = typeof(T);
            if (t == typeof(MeloChorus))             return new ChorusEffect();
            if (t == typeof(MeloReverb))             return new ReverbEffect();
            if (t == typeof(MeloDelay))              return new DelayEffect();
            if (t == typeof(MeloFilter))             return new FilterEffect();
            if (t == typeof(MeloEq))                 return new EqEffect();
            if (t == typeof(MeloConvolutionReverb))  return new ConvolutionReverbEffect();
            if (t == typeof(MeloVisualizer))         return new VisualizerEffect();
            throw new NotSupportedException($"AddEffect<{t.Name}>() is not supported.");
        }

        private static Type UnderlyingTypeFor<T>() where T : MeloEffect
        {
            var t = typeof(T);
            if (t == typeof(MeloChorus))             return typeof(ChorusEffect);
            if (t == typeof(MeloReverb))             return typeof(ReverbEffect);
            if (t == typeof(MeloDelay))              return typeof(DelayEffect);
            if (t == typeof(MeloFilter))             return typeof(FilterEffect);
            if (t == typeof(MeloEq))                 return typeof(EqEffect);
            if (t == typeof(MeloConvolutionReverb))  return typeof(ConvolutionReverbEffect);
            if (t == typeof(MeloVisualizer))         return typeof(VisualizerEffect);
            throw new NotSupportedException($"RemoveEffect<{t.Name}>() is not supported.");
        }
    }
}
