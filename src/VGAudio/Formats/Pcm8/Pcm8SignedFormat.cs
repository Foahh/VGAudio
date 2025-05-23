namespace VGAudio.Formats.Pcm8;

public class Pcm8SignedFormat : Pcm8Format
{
    public Pcm8SignedFormat()
    {
    }

    public Pcm8SignedFormat(byte[][] channels, int sampleRate) : base(new Pcm8FormatBuilder(channels, sampleRate))
    {
    }

    internal Pcm8SignedFormat(Pcm8FormatBuilder b) : base(b)
    {
    }

    public override bool Signed { get; } = true;
}