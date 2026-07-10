using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// Convolution reverb: uses a recorded impulse response for a real space. Heavier than
    /// MeloReverb. The IR is chosen in Melowrite; at runtime you only control mix.
    /// </summary>
    public sealed class MeloConvolutionReverb : MeloEffect
    {
        private readonly ConvolutionReverbEffect _fx;
        public MeloConvolutionReverb(ConvolutionReverbEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// wet/dry, 0-1
        /// </summary>
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        /// <summary>
        /// impulse response filename, set in Melowrite before export (read-only here)
        /// </summary>
        public string IrName => _fx.IrName;
    }
}
