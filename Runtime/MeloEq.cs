using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// 3 band, low shelf / mid peak / high shelf
    /// </summary>
    public sealed class MeloEq : MeloEffect
    {
        private readonly EqEffect _fx;
        public MeloEq(EqEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// hz
        /// </summary>
        public float LowFreq { get => _fx.LowFreq; set => _fx.LowFreq = value; }

        /// <summary>
        /// db, -12..12
        /// </summary>
        public float LowGain { get => _fx.LowGain; set => _fx.LowGain = value; }

        /// <summary>
        /// hz
        /// </summary>
        public float MidFreq { get => _fx.MidFreq; set => _fx.MidFreq = value; }

        /// <summary>
        /// db, -12..12
        /// </summary>
        public float MidGain { get => _fx.MidGain; set => _fx.MidGain = value; }

        /// <summary>
        /// 0.1-10, higher = narrower
        /// </summary>
        public float MidQ { get => _fx.MidQ; set => _fx.MidQ = value; }

        /// <summary>
        /// hz
        /// </summary>
        public float HighFreq { get => _fx.HighFreq; set => _fx.HighFreq = value; }

        /// <summary>
        /// db, -12..12
        /// </summary>
        public float HighGain { get => _fx.HighGain; set => _fx.HighGain = value; }
    }
}
