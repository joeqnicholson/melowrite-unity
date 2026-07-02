using System;
using System.Collections.Generic;
using Melowrite.Audio.Effects;
using Melowrite.Core;

namespace Melowrite.Audio
{
    // Typed handle to one bus in a loaded project. Buses are shared send destinations
    // (usually reverb, delay, and a master). Same effect access as MeloTrack.
    public readonly struct MeloBus
    {
        private readonly Track _bus;

        // index in the project's bus list (0 is master)
        public int Index { get; }

        public bool IsValid => _bus != null;

        internal MeloBus(Track bus, int index) { _bus = bus; Index = index; }

        public string Name => _bus?.Name ?? "";

        // 0-1
        public float Volume
        {
            get => _bus?.Volume ?? 0f;
            set { if (_bus != null) _bus.Volume = Math.Clamp(value, 0f, 1f); }
        }

        // -1 left, 0 center, +1 right
        public float Pan
        {
            get => _bus?.Pan ?? 0f;
            set { if (_bus != null) _bus.Pan = Math.Clamp(value, -1f, 1f); }
        }

        public bool Muted
        {
            get => _bus?.Muted ?? false;
            set { if (_bus != null) _bus.Muted = value; }
        }

        // -- Effects --

        public int EffectCount => _bus?.Effects.Count ?? 0;

        public IEnumerable<MeloEffect> Effects
        {
            get
            {
                if (_bus == null) yield break;
                foreach (var fx in _bus.Effects) yield return MeloEffect.Wrap(fx);
            }
        }

        public bool TryGetEffect<T>(out T effect) where T : MeloEffect
        {
            effect = GetEffect<T>();
            return effect != null;
        }

        public T? GetEffect<T>() where T : MeloEffect
        {
            if (_bus == null) return null;
            foreach (var fx in _bus.Effects)
            {
                var wrapped = MeloEffect.Wrap(fx);
                if (wrapped is T match) return match;
            }
            return null;
        }

        public bool HasEffect<T>() where T : MeloEffect => GetEffect<T>() != null;

        public T AddEffect<T>() where T : MeloEffect
        {
            if (_bus == null) throw new InvalidOperationException("Bus is invalid.");
            var raw = CreateUnderlyingFor<T>();
            _bus.Effects.Add(raw);
            return (T)MeloEffect.Wrap(raw);
        }

        public bool RemoveEffect<T>() where T : MeloEffect
        {
            if (_bus == null) return false;
            var concrete = UnderlyingTypeFor<T>();
            for (int i = 0; i < _bus.Effects.Count; i++)
                if (concrete.IsInstanceOfType(_bus.Effects[i]))
                {
                    _bus.Effects.RemoveAt(i);
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
