using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    // Stereo delay with feedback. For tempo-locked echoes set SyncEnabled and SyncDivisionIndex;
    // otherwise Interval sets the time in ms.
    public sealed class MeloDelay : MeloEffect
    {
        private readonly DelayEffect _fx;
        public MeloDelay(DelayEffect fx) : base(fx) { _fx = fx; }

        // delay time in ms when sync is off (~1-3000)
        public float Interval { get => _fx.Interval; set => _fx.Interval = value; }

        // ms for repeats to fade to silence
        public float DecayTime { get => _fx.DecayTime; set => _fx.DecayTime = value; }

        // wet/dry, 0-1
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        // alternate repeats left/right
        public bool PingPong { get => _fx.PingPong; set => _fx.PingPong = value; }

        // Hz where the feedback path rolls off the highs (default 8000)
        public float DampingFreq { get => _fx.DampingFreq; set => _fx.DampingFreq = value; }

        // when true, Interval is ignored and delay tracks tempo via SyncDivisionIndex
        public bool SyncEnabled { get => _fx.SyncEnabled; set => _fx.SyncEnabled = value; }

        // tempo-sync division index when SyncEnabled (3 = 1/4 by default)
        public int SyncDivisionIndex { get => _fx.SyncDivisionIndex; set => _fx.SyncDivisionIndex = value; }
    }
}
