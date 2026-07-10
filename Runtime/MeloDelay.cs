using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    /// <summary>
    /// Stereo delay with feedback. For tempo-locked echoes set SyncEnabled and SyncDivisionIndex;
    /// otherwise Interval sets the time in ms.
    /// </summary>
    public sealed class MeloDelay : MeloEffect
    {
        private readonly DelayEffect _fx;
        public MeloDelay(DelayEffect fx) : base(fx) { _fx = fx; }

        /// <summary>
        /// delay time in ms when sync is off (~1-3000)
        /// </summary>
        public float Interval { get => _fx.Interval; set => _fx.Interval = value; }

        /// <summary>
        /// ms for repeats to fade to silence
        /// </summary>
        public float DecayTime { get => _fx.DecayTime; set => _fx.DecayTime = value; }

        /// <summary>
        /// wet/dry, 0-1
        /// </summary>
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        /// <summary>
        /// alternate repeats left/right
        /// </summary>
        public bool PingPong { get => _fx.PingPong; set => _fx.PingPong = value; }

        /// <summary>
        /// Hz where the feedback path rolls off the highs (default 8000)
        /// </summary>
        public float DampingFreq { get => _fx.DampingFreq; set => _fx.DampingFreq = value; }

        /// <summary>
        /// when true, Interval is ignored and delay tracks tempo via SyncDivisionIndex
        /// </summary>
        public bool SyncEnabled { get => _fx.SyncEnabled; set => _fx.SyncEnabled = value; }

        /// <summary>
        /// tempo-sync division index when SyncEnabled (3 = 1/4 by default)
        /// </summary>
        public int SyncDivisionIndex { get => _fx.SyncDivisionIndex; set => _fx.SyncDivisionIndex = value; }
    }
}
