using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// resonant filter with env follower + lfo
    /// </summary>
    public sealed class MeloFilter : MeloEffect
    {
        private readonly FilterEffect _fx;
        public MeloFilter(FilterEffect fx) : base(fx) { _fx = fx; }

        public MeloFilterType Type
        {
            get => (MeloFilterType)_fx.Type;
            set => _fx.Type = (FilterType)value;
        }

        /// <summary>
        /// hz, 20-20000. lower = darker
        /// </summary>
        public float Cutoff { get => _fx.Cutoff; set => _fx.Cutoff = value; }

        /// <summary>
        /// Q, 0.5-20. high values whistle and can self oscillate
        /// </summary>
        public float Resonance { get => _fx.Resonance; set => _fx.Resonance = value; }

        /// <summary>
        /// wet/dry 0-1, default 1
        /// </summary>
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        /// <summary>
        /// env follower amount in semitones, 0-48
        /// </summary>
        public float EnvAmount { get => _fx.EnvAmount; set => _fx.EnvAmount = value; }

        /// <summary>
        /// seconds
        /// </summary>
        public float EnvAttack { get => _fx.EnvAttack; set => _fx.EnvAttack = value; }

        /// <summary>
        /// seconds
        /// </summary>
        public float EnvRelease { get => _fx.EnvRelease; set => _fx.EnvRelease = value; }

        /// <summary>
        /// hz, 0.05-20
        /// </summary>
        public float LfoRate { get => _fx.LfoRate; set => _fx.LfoRate = value; }

        /// <summary>
        /// semitones of cutoff, 0-48. 0 = lfo off
        /// </summary>
        public float LfoDepth { get => _fx.LfoDepth; set => _fx.LfoDepth = value; }

        /// <summary>
        /// true = ignore LfoRate and sync to tempo via LfoSyncDivisionIndex
        /// </summary>
        public bool LfoSyncEnabled { get => _fx.LfoSyncEnabled; set => _fx.LfoSyncEnabled = value; }

        /// <summary>
        /// 5 = 1/4 by default
        /// </summary>
        public int LfoSyncDivisionIndex { get => _fx.LfoSyncDivisionIndex; set => _fx.LfoSyncDivisionIndex = value; }

        /// <summary>
        /// live env value 0-1
        /// </summary>
        public float EnvelopeValue => _fx.EnvelopeValue;

        /// <summary>
        /// live lfo value -1..1
        /// </summary>
        public float LfoValue => _fx.LfoValue;
    }

    /// <summary>
    /// mirrors Effects.FilterType so callers dont need the internal namespace
    /// </summary>
    public enum MeloFilterType { Lowpass, Highpass, Bandpass, Notch }
}
