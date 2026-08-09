using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// Typed handle to one effect on a track or bus. Subclasses expose each effect's
    /// parameters as C# properties.
    /// </summary>
    public abstract class MeloEffect
    {
        /// <summary>
        /// The raw effect, for anything the wrapper doesn't surface.
        /// </summary>
        public IEffect Underlying { get; }

        protected MeloEffect(IEffect effect) { Underlying = effect; }

        public string Name => Underlying.Name;

        /// <summary>
        /// false = bypassed (audio passes through untouched)
        /// </summary>
        public bool Enabled
        {
            get => Underlying.Enabled;
            set => Underlying.Enabled = value;
        }

        public void Bypass() => Underlying.Enabled = false;
        public void UnBypass() => Underlying.Enabled = true;

        /// <summary>
        /// Clear internal state (delay lines, reverb tails, envelopes).
        /// </summary>
        public void Reset() => Underlying.Reset();

        /// <summary>
        /// Wrap a raw IEffect into its typed wrapper.
        /// </summary>
        public static MeloEffect Wrap(IEffect effect)
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
    /// Fallback for any IEffect without a typed wrapper.
    /// </summary>
    public sealed class MeloUnknownEffect : MeloEffect
    {
        public MeloUnknownEffect(IEffect e) : base(e) { }
    }
}
