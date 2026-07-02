using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    // Resonant filter with envelope follower and LFO.
    public sealed class MeloFilter : MeloEffect
    {
        private readonly FilterEffect _fx;
        public MeloFilter(FilterEffect fx) : base(fx) { _fx = fx; }

        public MeloFilterType Type
        {
            get => (MeloFilterType)_fx.Type;
            set => _fx.Type = (FilterType)value;
        }

        // cutoff in Hz (20-20000). lower = darker
        public float Cutoff { get => _fx.Cutoff; set => _fx.Cutoff = value; }

        // resonance / Q (0.5-20). high values whistle and can self-oscillate
        public float Resonance { get => _fx.Resonance; set => _fx.Resonance = value; }

        // wet/dry, 0-1 (default 1)
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        // envelope follower amount in semitones (0-48)
        public float EnvAmount { get => _fx.EnvAmount; set => _fx.EnvAmount = value; }

        // envelope attack in seconds
        public float EnvAttack { get => _fx.EnvAttack; set => _fx.EnvAttack = value; }

        // envelope release in seconds
        public float EnvRelease { get => _fx.EnvRelease; set => _fx.EnvRelease = value; }

        // LFO rate in Hz (0.05-20)
        public float LfoRate { get => _fx.LfoRate; set => _fx.LfoRate = value; }

        // LFO depth in semitones of cutoff (0-48). 0 disables the LFO
        public float LfoDepth { get => _fx.LfoDepth; set => _fx.LfoDepth = value; }

        // when true, LfoRate is ignored and the LFO syncs to tempo via LfoSyncDivisionIndex
        public bool LfoSyncEnabled { get => _fx.LfoSyncEnabled; set => _fx.LfoSyncEnabled = value; }

        // LFO sync division index when sync is on (5 = 1/4 by default)
        public int LfoSyncDivisionIndex { get => _fx.LfoSyncDivisionIndex; set => _fx.LfoSyncDivisionIndex = value; }

        // live envelope value (0-1), read-only
        public float EnvelopeValue => _fx.EnvelopeValue;

        // live LFO value (-1 to +1), read-only
        public float LfoValue => _fx.LfoValue;
    }

    // Mirror of Melowrite.Audio.Effects.FilterType so callers don't import the internal namespace.
    public enum MeloFilterType { Lowpass, Highpass, Bandpass, Notch }
}
