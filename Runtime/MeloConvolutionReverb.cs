using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// impulse response reverb, heavier than MeloReverb. IR is picked in melowrite, only mix is live
    /// </summary>
    public sealed class MeloConvolutionReverb : MeloEffect
    {
        private readonly ConvolutionReverbEffect _fx;
        public MeloConvolutionReverb(ConvolutionReverbEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// wet/dry 0-1
        /// </summary>
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        /// <summary>
        /// set in melowrite before export
        /// </summary>
        public string IrName => _fx.IrName;
    }
}
