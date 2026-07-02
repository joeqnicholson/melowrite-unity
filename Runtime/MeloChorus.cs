using Melowrite.Audio.Effects;

namespace Melowrite.Audio
{
    // Stereo chorus: modulated short delay lines that thicken a sound.
    public sealed class MeloChorus : MeloEffect
    {
        private readonly ChorusEffect _fx;
        public MeloChorus(ChorusEffect fx) : base(fx) { _fx = fx; }

        // LFO rate in Hz (0.1-5)
        public float Rate { get => _fx.Rate; set => _fx.Rate = value; }

        // depth in ms (0.5-10)
        public float Depth { get => _fx.Depth; set => _fx.Depth = value; }

        // wet/dry, 0-1
        public float Mix { get => _fx.Mix; set => _fx.Mix = value; }

        // feedback (0-0.8). higher adds a flanger-like character
        public float Feedback { get => _fx.Feedback; set => _fx.Feedback = value; }

        // voice count (1-3)
        public int Voices { get => _fx.Voices; set => _fx.Voices = value; }
    }
}
