using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// handle to one effect on a track or bus. subclasses expose the params as properties
    /// </summary>
    public abstract class MeloEffect
    {
        /// <summary>
        /// raw effect for whatever the wrapper doesnt cover
        /// </summary>
        public Effect Underlying { get; }

        protected MeloEffect(Effect effect) { Underlying = effect; }

        public string Name => Underlying.Name;

        /// <summary>
        /// false = bypassed
        /// </summary>
        public bool Enabled
        {
            get => Underlying.Enabled;
            set => Underlying.Enabled = value;
        }

        public void Bypass() => Underlying.Enabled = false;
        public void UnBypass() => Underlying.Enabled = true;

        /// <summary>
        /// clears delay lines, tails, envelopes
        /// </summary>
        public void Reset() => Underlying.Reset();

        /// <summary>
        /// raw Effect -> typed wrapper
        /// </summary>
        public static MeloEffect Wrap(Effect effect)
        {
            return effect switch
            {
                ChorusEffect c             => new MeloChorus(c),
                ReverbEffect r             => new MeloReverb(r),
                DelayEffect d              => new MeloDelay(d),
                FilterEffect f             => new MeloFilter(f),
                EqEffect e                 => new MeloEq(e),
                ConvolutionReverbEffect cr => new MeloConvolutionReverb(cr),
                VisualizerEffect v         => new MeloVisualizer(v),
                _                          => new MeloUnknownEffect(effect),
            };
        }
    }

    /// <summary>
    /// fallback for anything without a wrapper
    /// </summary>
    public sealed class MeloUnknownEffect : MeloEffect
    {
        public MeloUnknownEffect(Effect e) : base(e) { }
    }
}
