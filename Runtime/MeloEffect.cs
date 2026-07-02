using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    // Typed handle to one effect on a track or bus. Subclasses expose each effect's
    // parameters as C# properties.
    public abstract class MeloEffect
    {
        // The raw effect, for anything the wrapper doesn't surface.
        public IEffect Underlying { get; }

        protected MeloEffect(IEffect effect) { Underlying = effect; }

        public string Name => Underlying.Name;

        // false = bypassed (audio passes through untouched)
        public bool Enabled
        {
            get => Underlying.Enabled;
            set => Underlying.Enabled = value;
        }

        public void Bypass() => Underlying.Enabled = false;
        public void UnBypass() => Underlying.Enabled = true;

        // Clear internal state (delay lines, reverb tails, envelopes).
        public void Reset() => Underlying.Reset();

        // Wrap a raw IEffect into its typed wrapper.
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

    // Fallback for any IEffect without a typed wrapper.
    public sealed class MeloUnknownEffect : MeloEffect
    {
        public MeloUnknownEffect(IEffect e) : base(e) { }
    }
}
