using System;
using System.Collections.Generic;
using System.IO;
using static VGAudio.Utilities.Helpers;

namespace VGAudio.Utilities.Riff;

public class RiffParser
{
    public RiffChunk RiffChunk { get; set; }
    public bool ReadDataChunk { get; set; } = true;
    private Dictionary<string, RiffSubChunk> SubChunks { get; } = [];

    private Dictionary<string, Func<RiffParser, BinaryReader, RiffSubChunk>> RegisteredSubChunks { get; } =
        new()
        {
            ["fmt "] = WaveFmtChunk.Parse,
            ["smpl"] = WaveSmplChunk.Parse,
            ["fact"] = WaveFactChunk.Parse,
            ["data"] = WaveDataChunk.Parse
        };

    public Func<RiffParser, BinaryReader, WaveFormatExtensible> FormatExtensibleParser { get; set; } = WaveFormatExtensible.Parse;

    public void RegisterSubChunk(string id, Func<RiffParser, BinaryReader, RiffSubChunk> subChunkReader)
    {
        if (id.Length != 4)
        {
            throw new NotSupportedException("Subchunk ID must be 4 characters long");
        }

        RegisteredSubChunks[id] = subChunkReader;
    }

    public void ParseRiff(Stream file)
    {
        using var reader = GetBinaryReader(file, Endianness.LittleEndian);
        RiffChunk = RiffChunk.Parse(reader);
        SubChunks.Clear();

        // Size is counted from after the ChunkSize field, not the RiffType field
        var startOffset = reader.BaseStream.Position - 4;
        var endOffset = startOffset + RiffChunk.Size;

        // Make sure 8 bytes are available for the subchunk header
        while (reader.BaseStream.Position + 8 < endOffset)
        {
            var subChunk = ParseSubChunk(reader);
            SubChunks[subChunk.SubChunkId] = subChunk;
        }
    }

    public List<RiffSubChunk> GetAllSubChunks()
    {
        return [.. SubChunks.Values];
    }

    public T GetSubChunk<T>(string id) where T : RiffSubChunk
    {
        SubChunks.TryGetValue(id, out var chunk);
        return chunk as T;
    }

    private RiffSubChunk ParseSubChunk(BinaryReader reader)
    {
        var id = reader.ReadUtf8(4);
        reader.BaseStream.Position -= 4;
        var startOffset = reader.BaseStream.Position + 8;

        var subChunk = RegisteredSubChunks.TryGetValue(id, out var parser) ? parser(this, reader) : new RiffSubChunk(reader);

        var endOffset = startOffset + subChunk.SubChunkSize;
        var remainingBytes = (int)Math.Max(endOffset - reader.BaseStream.Position, 0);
        subChunk.Extra = reader.ReadBytes(remainingBytes);

        reader.BaseStream.Position = endOffset + (endOffset & 1); // Subchunks are 2-byte aligned
        return subChunk;
    }
}