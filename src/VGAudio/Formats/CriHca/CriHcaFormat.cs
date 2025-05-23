using System;
using VGAudio.Codecs;
using VGAudio.Codecs.CriHca;
using VGAudio.Formats.Pcm16;
using VGAudio.Utilities;
using static VGAudio.Codecs.CriHca.CriHcaConstants;

namespace VGAudio.Formats.CriHca;

public class CriHcaFormat : AudioFormatBase<CriHcaFormat, CriHcaFormatBuilder, CriHcaParameters>
{
    public CriHcaFormat()
    {
    }

    internal CriHcaFormat(CriHcaFormatBuilder b) : base(b)
    {
        AudioData = b.AudioData;
        Hca = b.Hca;
    }

    public HcaInfo Hca { get; }

    public byte[][] AudioData { get; }

    public override Pcm16Format ToPcm16()
    {
        return ToPcm16(null);
    }

    public override Pcm16Format ToPcm16(CodecParameters config)
    {
        return ToPcm16(new CriHcaParameters(config));
    }

    public override Pcm16Format ToPcm16(CriHcaParameters config)
    {
        var audio = CriHcaDecoder.Decode(Hca, AudioData, config);
        return new Pcm16FormatBuilder(audio, SampleRate)
            .WithLoop(Looping, UnalignedLoopStart, UnalignedLoopEnd)
            .Build();
    }

    public override CriHcaFormat EncodeFromPcm16(Pcm16Format pcm16, CriHcaParameters config)
    {
        config.ChannelCount = pcm16.ChannelCount;
        config.SampleRate = pcm16.SampleRate;
        config.SampleCount = pcm16.SampleCount;
        config.Looping = pcm16.Looping;
        config.LoopStart = pcm16.LoopStart;
        config.LoopEnd = pcm16.LoopEnd;
        var progress = config.Progress;

        var encoder = CriHcaEncoder.InitializeNew(config);
        var pcm = pcm16.Channels;
        var pcmBuffer = Helpers.CreateJaggedArray<short[][]>(pcm16.ChannelCount, SamplesPerFrame);

        progress?.SetTotal(encoder.Hca.FrameCount);

        var audio = Helpers.CreateJaggedArray<byte[][]>(encoder.Hca.FrameCount, encoder.FrameSize);

        var frameNum = 0;
        for (var i = 0; frameNum < encoder.Hca.FrameCount; i++)
        {
            var samplesToCopy = Math.Min(pcm16.SampleCount - i * SamplesPerFrame, SamplesPerFrame);
            for (var c = 0; c < pcm.Length; c++)
            {
                Array.Copy(pcm[c], SamplesPerFrame * i, pcmBuffer[c], 0, samplesToCopy);
            }

            var framesWritten = encoder.Encode(pcmBuffer, audio[frameNum]);
            if (framesWritten == 0)
            {
                throw new NotSupportedException("Encoder returned no audio. This should not happen.");
            }

            if (framesWritten > 0)
            {
                frameNum++;
                framesWritten--;
                progress?.ReportAdd(1);
            }

            while (framesWritten > 0)
            {
                audio[frameNum] = encoder.GetPendingFrame();
                frameNum++;
                framesWritten--;
                progress?.ReportAdd(1);
            }
        }
        var builder = new CriHcaFormatBuilder(audio, encoder.Hca);
        return builder.Build();
    }

    public override CriHcaFormat EncodeFromPcm16(Pcm16Format pcm16)
    {
        var config = new CriHcaParameters
        {
            ChannelCount = pcm16.ChannelCount,
            SampleRate = pcm16.SampleRate
        };

        return EncodeFromPcm16(pcm16, config);
    }

    protected override CriHcaFormat GetChannelsInternal(int[] channelRange)
    {
        throw new NotImplementedException();
    }

    protected override CriHcaFormat AddInternal(CriHcaFormat format)
    {
        throw new NotImplementedException();
    }

    public override CriHcaFormatBuilder GetCloneBuilder()
    {
        return new CriHcaFormatBuilder(AudioData, Hca.GetClone());
    }
}