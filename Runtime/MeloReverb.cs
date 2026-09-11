using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// algorithmic reverb. MeloConvolutionReverb for a real space
    /// </summary>
    public sealed class MeloReverb : MeloEffect
    {
        private readonly ReverbEffect _fx;
        public MeloReverb(ReverbEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// 0-1, room to hall
        /// </summary>
        public float RoomSize { get => _fx.RoomSize; set => _fx.RoomSize = value; }

        /// <summary>
        /// 0-1, higher = darker
        /// </summary>
        public float Damping { get => _fx.Damping; set => _fx.Damping = value; }

        /// <summary>
        /// wet/dry 0-1
        /// </summary>
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }
    }
}
