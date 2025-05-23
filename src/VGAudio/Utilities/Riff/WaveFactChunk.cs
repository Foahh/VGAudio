using System.IO;

namespace VGAudio.Utilities.Riff;

public class WaveFactChunk : RiffSubChunk
{
    protected WaveFactChunk(BinaryReader reader) : base(reader)
    {
        SampleCount = reader.ReadInt32();
    }

    public int SampleCount { get; set; }

    public static WaveFactChunk Parse(RiffParser parser, BinaryReader reader)
    {
        return new WaveFactChunk(reader);
    }
}