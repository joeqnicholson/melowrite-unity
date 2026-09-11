using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// stereo chorus
    /// </summary>
    public sealed class MeloChorus : MeloEffect
    {
        private readonly ChorusEffect _fx;
        public MeloChorus(ChorusEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// hz, 0.1-5
        /// </summary>
        public float Rate { get => _fx.Rate; set => _fx.Rate = value; }

        /// <summary>
        /// ms, 0.5-10
        /// </summary>
        public float Depth { get => _fx.Depth; set => _fx.Depth = value; }

        /// <summary>
        /// wet/dry 0-1
        /// </summary>
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        /// <summary>
        /// 0-0.8, higher goes flangery
        /// </summary>
        public float Feedback { get => _fx.Feedback; set => _fx.Feedback = value; }

        /// <summary>
        /// 1-3
        /// </summary>
        public int Voices { get => _fx.Voices; set => _fx.Voices = value; }
    }
}
