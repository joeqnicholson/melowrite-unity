using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    // Pass-through tap that captures audio for visualization. Doesn't change the signal.
    public sealed class MeloVisualizer : MeloEffect
    {
        private readonly VisualizerEffect _fx;
        public MeloVisualizer(VisualizerEffect fx) : base(fx) { _fx = fx; }

        // smoothing for visualizer reads (0.01-1). lower = smoother, higher = snappier
        public float WaveLerpSpeed { get => _fx.WaveLerpSpeed; set => _fx.WaveLerpSpeed = value; }

        // rolling sample buffer used by the visualizer
        public float[] Buffer => _fx.Buffer;
    }
}
