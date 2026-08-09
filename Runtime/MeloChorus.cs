using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// Stereo chorus: modulated short delay lines that thicken a sound.
    /// </summary>
    public sealed class MeloChorus : MeloEffect
    {
        private readonly ChorusEffect _fx;
        public MeloChorus(ChorusEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// LFO rate in Hz (0.1-5)
        /// </summary>
        public float Rate { get => _fx.Rate; set => _fx.Rate = value; }

        /// <summary>
        /// depth in ms (0.5-10)
        /// </summary>
        public float Depth { get => _fx.Depth; set => _fx.Depth = value; }

        /// <summary>
        /// wet/dry, 0-1
        /// </summary>
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        /// <summary>
        /// feedback (0-0.8). higher adds a flanger-like character
        /// </summary>
        public float Feedback { get => _fx.Feedback; set => _fx.Feedback = value; }

        /// <summary>
        /// voice count (1-3)
        /// </summary>
        public int Voices { get => _fx.Voices; set => _fx.Voices = value; }
    }
}
