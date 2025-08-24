using System;
using System.Collections.Generic;
using System.Linq;
using VGAudio.Codecs;
using VGAudio.Formats.Pcm16;

namespace VGAudio.Formats;

public abstract class AudioFormatBase<TFormat, TBuilder, TConfig> : IAudioFormat
    where TFormat : AudioFormatBase<TFormat, TBuilder, TConfig>
    where TBuilder : AudioFormatBaseBuilder<TFormat, TBuilder, TConfig>
    where TConfig : CodecParameters, new()
{
    private readonly List<AudioTrack> _tracks;

    protected AudioFormatBase()
    {
    }

    protected AudioFormatBase(TBuilder builder)
    {
        UnalignedSampleCount = builder.SampleCount;
        SampleRate = builder.SampleRate;
        ChannelCount = builder.ChannelCount;
        Looping = builder.Looping;
        UnalignedLoopStart = builder.LoopStart;
        UnalignedLoopEnd = builder.LoopEnd;
        _tracks = builder.Tracks;
        Tracks = _tracks != null && _tracks.Count > 0 ? _tracks : [.. AudioTrack.GetDefaultTrackList(ChannelCount)];
    }

    public int UnalignedSampleCount { get; }
    public int UnalignedLoopStart { get; }
    public int UnalignedLoopEnd { get; }
    public List<AudioTrack> Tracks { get; }
    public int SampleRate { get; }
    public int ChannelCount { get; }
    public virtual int SampleCount => UnalignedSampleCount;
    public virtual int LoopStart => UnalignedLoopStart;
    public virtual int LoopEnd => UnalignedLoopEnd;
    public bool Looping { get; }

    IAudioFormat IAudioFormat.EncodeFromPcm16(Pcm16Format pcm16)
    {
        return EncodeFromPcm16(pcm16);
    }

    IAudioFormat IAudioFormat.EncodeFromPcm16(Pcm16Format pcm16, CodecParameters config)
    {
        return EncodeFromPcm16(pcm16, GetDerivedParameters(config));
    }

    IAudioFormat IAudioFormat.GetChannels(params int[] channelRange)
    {
        return GetChannels(channelRange);
    }

    IAudioFormat IAudioFormat.WithLoop(bool loop, int loopStart, int loopEnd)
    {
        return WithLoop(loop, loopStart, loopEnd);
    }

    IAudioFormat IAudioFormat.WithLoop(bool loop)
    {
        return WithLoop(loop);
    }

    public abstract Pcm16Format ToPcm16();

    public virtual Pcm16Format ToPcm16(CodecParameters config)
    {
        return ToPcm16();
    }

    public bool TryAdd(IAudioFormat format, out IAudioFormat result)
    {
        result = null;
        var castFormat = format as TFormat;
        if (castFormat == null) return false;
        try
        {
            result = Add(castFormat);
        }
        catch
        {
            return false;
        }
        return true;
    }

    public virtual Pcm16Format ToPcm16(TConfig config)
    {
        return ToPcm16((CodecParameters)config);
    }

    public abstract TFormat EncodeFromPcm16(Pcm16Format pcm16);

    public virtual TFormat EncodeFromPcm16(Pcm16Format pcm16, TConfig config)
    {
        return EncodeFromPcm16(pcm16);
    }

    public TFormat GetChannels(params int[] channelRange)
    {
        if (channelRange == null)
            throw new ArgumentNullException(nameof(channelRange));

        return GetChannelsInternal(channelRange);
    }

    protected abstract TFormat GetChannelsInternal(int[] channelRange);

    public virtual TFormat WithLoop(bool loop)
    {
        return GetCloneBuilder().WithLoop(loop).Build();
    }

    public virtual TFormat WithLoop(bool loop, int loopStart, int loopEnd)
    {
        return GetCloneBuilder().WithLoop(loop, loopStart, loopEnd).Build();
    }

    public virtual TFormat Add(TFormat format)
    {
        if (format.UnalignedSampleCount != UnalignedSampleCount)
        {
            throw new ArgumentException("Only audio streams of the same length can be added to each other.");
        }

        return AddInternal(format);
    }

    protected abstract TFormat AddInternal(TFormat format);
    public abstract TBuilder GetCloneBuilder();

    protected TBuilder GetCloneBuilderBase(TBuilder builder)
    {
        builder.SampleCount = UnalignedSampleCount;
        builder.SampleRate = SampleRate;
        builder.Looping = Looping;
        builder.LoopStart = UnalignedLoopStart;
        builder.LoopEnd = UnalignedLoopEnd;
        builder.Tracks = _tracks;
        return builder;
    }

    private TConfig GetDerivedParameters(CodecParameters param)
    {
        if (param == null) return null;
        var config = param as TConfig;
        if (config != null) return config;

        return new TConfig
        {
            SampleCount = param.SampleCount,
            Progress = param.Progress
        };
    }
}