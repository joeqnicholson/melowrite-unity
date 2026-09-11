using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// stereo delay. SyncEnabled + SyncDivisionIndex for tempo locked repeats, otherwise Interval in ms
    /// </summary>
    public sealed class MeloDelay : MeloEffect
    {
        private readonly DelayEffect _fx;
        public MeloDelay(DelayEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// ms when sync is off, ~1-3000
        /// </summary>
        public float Interval { get => _fx.Interval; set => _fx.Interval = value; }

        /// <summary>
        /// ms for repeats to die out
        /// </summary>
        public float DecayTime { get => _fx.DecayTime; set => _fx.DecayTime = value; }

        /// <summary>
        /// wet/dry 0-1
        /// </summary>
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        /// <summary>
        /// alternate repeats left/right
        /// </summary>
        public bool PingPong { get => _fx.PingPong; set => _fx.PingPong = value; }

        /// <summary>
        /// hz where the feedback path rolls off, default 8000
        /// </summary>
        public float DampingFreq { get => _fx.DampingFreq; set => _fx.DampingFreq = value; }

        /// <summary>
        /// true = ignore Interval and track tempo via SyncDivisionIndex
        /// </summary>
        public bool SyncEnabled { get => _fx.SyncEnabled; set => _fx.SyncEnabled = value; }

        /// <summary>
        /// 3 = 1/4 by default
        /// </summary>
        public int SyncDivisionIndex { get => _fx.SyncDivisionIndex; set => _fx.SyncDivisionIndex = value; }
    }
}
