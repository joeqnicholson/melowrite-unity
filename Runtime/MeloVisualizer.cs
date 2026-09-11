using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// pass through tap for visualizers, doesnt touch the signal
    /// </summary>
    public sealed class MeloVisualizer : MeloEffect
    {
        private readonly VisualizerEffect _fx;
        public MeloVisualizer(VisualizerEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// 0.01-1, lower = smoother
        /// </summary>
        public float WaveLerpSpeed { get => _fx.WaveLerpSpeed; set => _fx.WaveLerpSpeed = value; }

        /// <summary>
        /// rolling sample buffer
        /// </summary>
        public float[] Buffer => _fx.Buffer;
    }
}
