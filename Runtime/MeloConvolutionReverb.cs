using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    // Convolution reverb: uses a recorded impulse response for a real space. Heavier than
    // MeloReverb. The IR is chosen in Melowrite; at runtime you only control mix.
    public sealed class MeloConvolutionReverb : MeloEffect
    {
        private readonly ConvolutionReverbEffect _fx;
        public MeloConvolutionReverb(ConvolutionReverbEffect fx) : base(fx) { _fx = fx; }

        // wet/dry, 0-1
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        // impulse response filename, set in Melowrite before export (read-only here)
        public string IrName => _fx.IrName;
    }
}
