using System;
using System.Collections.Generic;
using VGAudio.Codecs;

namespace VGAudio.Formats.Pcm16;

/// <summary>
///     A 16-bit PCM audio stream.
///     The stream can contain any number of individual channels.
/// </summary>
public class Pcm16Format : AudioFormatBase<Pcm16Format, Pcm16FormatBuilder, CodecParameters>
{
    public Pcm16Format()
    {
        Channels = [];
    }

    public Pcm16Format(short[][] channels, int sampleRate) : this(new Pcm16FormatBuilder(channels, sampleRate))
    {
    }

    internal Pcm16Format(Pcm16FormatBuilder b) : base(b)
    {
        Channels = b.Channels;
    }

    public short[][] Channels { get; }

    public override Pcm16Format ToPcm16()
    {
        return GetCloneBuilder().Build();
    }

    public override Pcm16Format EncodeFromPcm16(Pcm16Format pcm16)
    {
        return pcm16.GetCloneBuilder().Build();
    }

    protected override Pcm16Format AddInternal(Pcm16Format pcm16)
    {
        var copy = GetCloneBuilder();
        copy.Channels = [.. Channels, .. pcm16.Channels];
        return copy.Build();
    }

    protected override Pcm16Format GetChannelsInternal(int[] channelRange)
    {
        var channels = new List<short[]>();

        foreach (var i in channelRange)
        {
            if (i < 0 || i >= Channels.Length)
                throw new ArgumentException($"Channel {i} does not exist.", nameof(channelRange));
            channels.Add(Channels[i]);
        }

        var copy = GetCloneBuilder();
        copy.Channels = [.. channels];
        return copy.Build();
    }

    public static Pcm16FormatBuilder GetBuilder(short[][] channels, int sampleRate)
    {
        return new Pcm16FormatBuilder(channels, sampleRate);
    }

    public override Pcm16FormatBuilder GetCloneBuilder()
    {
        return GetCloneBuilderBase(new Pcm16FormatBuilder(Channels, SampleRate));
    }
}