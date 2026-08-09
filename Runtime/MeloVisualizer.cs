using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// Pass-through tap that captures audio for visualization. Doesn't change the signal.
    /// </summary>
    public sealed class MeloVisualizer : MeloEffect
    {
        private readonly VisualizerEffect _fx;
        public MeloVisualizer(VisualizerEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// smoothing for visualizer reads (0.01-1). lower = smoother, higher = snappier
        /// </summary>
        public float WaveLerpSpeed { get => _fx.WaveLerpSpeed; set => _fx.WaveLerpSpeed = value; }

        /// <summary>
        /// rolling sample buffer used by the visualizer
        /// </summary>
        public float[] Buffer => _fx.Buffer;
    }
}
