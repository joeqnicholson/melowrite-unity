using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    // Algorithmic reverb. For a real recorded space use MeloConvolutionReverb.
    public sealed class MeloReverb : MeloEffect
    {
        private readonly ReverbEffect _fx;
        public MeloReverb(ReverbEffect fx) : base(fx) { _fx = fx; }

        // 0-1, small room to large hall
        public float RoomSize { get => _fx.RoomSize; set => _fx.RoomSize = value; }

        // high-frequency damping, 0-1 (higher = darker)
        public float Damping { get => _fx.Damping; set => _fx.Damping = value; }

        // wet/dry, 0-1
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }
    }
}
