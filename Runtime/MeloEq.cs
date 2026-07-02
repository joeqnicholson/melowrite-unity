using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    // Three-band parametric EQ: low shelf, mid peak, high shelf.
    public sealed class MeloEq : MeloEffect
    {
        private readonly EqEffect _fx;
        public MeloEq(EqEffect fx) : base(fx) { _fx = fx; }

        // low shelf corner in Hz
        public float LowFreq { get => _fx.LowFreq; set => _fx.LowFreq = value; }

        // low shelf gain in dB (-12 to +12)
        public float LowGain { get => _fx.LowGain; set => _fx.LowGain = value; }

        // mid peak center in Hz
        public float MidFreq { get => _fx.MidFreq; set => _fx.MidFreq = value; }

        // mid peak gain in dB (-12 to +12)
        public float MidGain { get => _fx.MidGain; set => _fx.MidGain = value; }

        // mid peak Q / bandwidth (0.1-10). higher = narrower
        public float MidQ { get => _fx.MidQ; set => _fx.MidQ = value; }

        // high shelf corner in Hz
        public float HighFreq { get => _fx.HighFreq; set => _fx.HighFreq = value; }

        // high shelf gain in dB (-12 to +12)
        public float HighGain { get => _fx.HighGain; set => _fx.HighGain = value; }
    }
}
