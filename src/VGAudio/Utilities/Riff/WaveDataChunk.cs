using System.IO;

namespace VGAudio.Utilities.Riff;

public class WaveDataChunk : RiffSubChunk
{
    protected WaveDataChunk(RiffParser parser, BinaryReader reader) : base(reader)
    {
        if (parser.ReadDataChunk)
        {
            Data = reader.ReadBytes(SubChunkSize);
        }
    }

    public byte[] Data { get; set; }

    public static WaveDataChunk Parse(RiffParser parser, BinaryReader reader)
    {
        return new WaveDataChunk(parser, reader);
    }
}