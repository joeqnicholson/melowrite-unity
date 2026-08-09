using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// Three-band parametric EQ: low shelf, mid peak, high shelf.
    /// </summary>
    public sealed class MeloEq : MeloEffect
    {
        private readonly EqEffect _fx;
        public MeloEq(EqEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// low shelf corner in Hz
        /// </summary>
        public float LowFreq { get => _fx.LowFreq; set => _fx.LowFreq = value; }

        /// <summary>
        /// low shelf gain in dB (-12 to +12)
        /// </summary>
        public float LowGain { get => _fx.LowGain; set => _fx.LowGain = value; }

        /// <summary>
        /// mid peak center in Hz
        /// </summary>
        public float MidFreq { get => _fx.MidFreq; set => _fx.MidFreq = value; }

        /// <summary>
        /// mid peak gain in dB (-12 to +12)
        /// </summary>
        public float MidGain { get => _fx.MidGain; set => _fx.MidGain = value; }

        /// <summary>
        /// mid peak Q / bandwidth (0.1-10). higher = narrower
        /// </summary>
        public float MidQ { get => _fx.MidQ; set => _fx.MidQ = value; }

        /// <summary>
        /// high shelf corner in Hz
        /// </summary>
        public float HighFreq { get => _fx.HighFreq; set => _fx.HighFreq = value; }

        /// <summary>
        /// high shelf gain in dB (-12 to +12)
        /// </summary>
        public float HighGain { get => _fx.HighGain; set => _fx.HighGain = value; }
    }
}
