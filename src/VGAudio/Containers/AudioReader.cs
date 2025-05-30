using System;
using System.IO;
using VGAudio.Formats;

namespace VGAudio.Containers;

public abstract class AudioReader<TReader, TStructure, TConfig> : IAudioReader
    where TReader : AudioReader<TReader, TStructure, TConfig>
    where TConfig : Configuration, new()
{
    public IAudioFormat ReadFormat(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadStream(stream).AudioFormat;
    }
    
    public IAudioFormat ReadFormat(Stream stream)
    {
        return ReadStream(stream).AudioFormat;
    }

    public IAudioFormat ReadFormat(byte[] file)
    {
        return ReadByteArray(file).AudioFormat;
    }

    public AudioData Read(Stream stream)
    {
        return ReadStream(stream).Audio;
    }

    public AudioData Read(byte[] file)
    {
        return ReadByteArray(file).Audio;
    }

    public AudioWithConfig ReadWithConfig(Stream stream)
    {
        return ReadStream(stream);
    }

    public AudioWithConfig ReadWithConfig(byte[] file)
    {
        return ReadByteArray(file);
    }

    public TStructure ReadMetadata(Stream stream)
    {
        return ReadStructure(stream, false);
    }

    protected virtual TConfig GetConfiguration(TStructure structure)
    {
        return new TConfig();
    }

    protected abstract TStructure ReadFile(Stream stream, bool readAudioData = true);
    protected abstract IAudioFormat ToAudioStream(TStructure structure);

    private AudioWithConfig ReadByteArray(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return ReadStream(stream);
    }

    private AudioWithConfig ReadStream(Stream stream)
    {
        var structure = ReadStructure(stream);
        return new AudioWithConfig(ToAudioStream(structure), GetConfiguration(structure));
    }

    private TStructure ReadStructure(Stream stream, bool readAudioData = true)
    {
        if (!stream.CanSeek)
        {
            throw new NotSupportedException("A seekable stream is required");
        }

        return ReadFile(stream, readAudioData);
    }
}