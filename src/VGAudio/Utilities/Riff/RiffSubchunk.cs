using System.IO;

namespace VGAudio.Utilities.Riff;

public class RiffSubChunk(BinaryReader reader)
{
    public string SubChunkId { get; set; } = reader.ReadUtf8(4);
    public int SubChunkSize { get; set; } = reader.ReadInt32();
    public byte[] Extra { get; set; }
}