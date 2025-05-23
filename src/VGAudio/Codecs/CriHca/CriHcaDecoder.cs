using System;
using VGAudio.Utilities;
using static VGAudio.Codecs.CriHca.CriHcaConstants;
using static VGAudio.Codecs.CriHca.CriHcaPacking;
using static VGAudio.Codecs.CriHca.CriHcaTables;

namespace VGAudio.Codecs.CriHca;

public static class CriHcaDecoder
{
    public static short[][] Decode(HcaInfo hca, byte[][] audio, CriHcaParameters config = null)
    {
        config?.Progress?.SetTotal(hca.FrameCount);
        var pcmOut = Helpers.CreateJaggedArray<short[][]>(hca.ChannelCount, hca.SampleCount);
        var pcmBuffer = Helpers.CreateJaggedArray<short[][]>(hca.ChannelCount, SamplesPerFrame);

        var frame = new CriHcaFrame(hca);

        for (var i = 0; i < hca.FrameCount; i++)
        {
            DecodeFrame(audio[i], frame, pcmBuffer);

            CopyPcmToOutput(pcmBuffer, pcmOut, hca, i);
            //CopyBuffer(pcmBuffer, pcmOut, hca.InsertedSamples, i);
            config?.Progress?.ReportAdd(1);
        }

        return pcmOut;
    }

    private static void CopyPcmToOutput(short[][] pcmIn, short[][] pcmOut, HcaInfo hca, int frame)
    {
        var currentSample = frame * SamplesPerFrame - hca.InsertedSamples;
        var remainingSamples = Math.Min(hca.SampleCount - currentSample, hca.SampleCount);
        var srcStart = Helpers.Clamp(0 - currentSample, 0, SamplesPerFrame);
        var destStart = Math.Max(currentSample, 0);

        var length = Math.Min(SamplesPerFrame - srcStart, remainingSamples);
        if (length <= 0) return;

        for (var c = 0; c < pcmOut.Length; c++)
        {
            Array.Copy(pcmIn[c], srcStart, pcmOut[c], destStart, length);
        }
    }

    public static void CopyBuffer(short[][] bufferIn, short[][] bufferOut, int startIndex, int bufferIndex)
    {
        if (bufferIn == null || bufferOut == null || bufferIn.Length == 0 || bufferOut.Length == 0)
        {
            throw new ArgumentException(
                $"{nameof(bufferIn)} and {nameof(bufferOut)} must be non-null with a length greater than 0");
        }

        var bufferLength = bufferIn[0].Length;
        var outLength = bufferOut[0].Length;

        var currentIndex = bufferIndex * bufferLength - startIndex;
        var remainingElements = Math.Min(outLength - currentIndex, outLength);
        var srcStart = Helpers.Clamp(0 - currentIndex, 0, SamplesPerFrame);
        var destStart = Math.Max(currentIndex, 0);

        var length = Math.Min(SamplesPerFrame - srcStart, remainingElements);
        if (length <= 0) return;

        for (var c = 0; c < bufferOut.Length; c++)
        {
            Array.Copy(bufferIn[c], srcStart, bufferOut[c], destStart, length);
        }
    }

    private static void DecodeFrame(byte[] audio, CriHcaFrame frame, short[][] pcmOut)
    {
        var reader = new BitReader(audio);

        UnpackFrame(frame, reader);
        DequantizeFrame(frame);
        RestoreMissingBands(frame);
        RunImdct(frame);
        PcmFloatToShort(frame, pcmOut);
    }

    private static void DequantizeFrame(CriHcaFrame frame)
    {
        foreach (var channel in frame.Channels)
        {
            CalculateGain(channel);
        }

        for (var sf = 0; sf < SubframesPerFrame; sf++)
        {
            foreach (var channel in frame.Channels)
            {
                for (var s = 0; s < channel.CodedScaleFactorCount; s++)
                {
                    channel.Spectra[sf][s] = channel.QuantizedSpectra[sf][s] * channel.Gain[s];
                }
            }
        }
    }

    private static void RestoreMissingBands(CriHcaFrame frame)
    {
        ReconstructHighFrequency(frame);
        ApplyIntensityStereo(frame);
    }

    private static void CalculateGain(CriHcaChannel channel)
    {
        for (var i = 0; i < channel.CodedScaleFactorCount; i++)
        {
            channel.Gain[i] = DequantizerScalingTable[channel.ScaleFactors[i]] * QuantizerStepSize[channel.Resolution[i]];
        }
    }

    private static void ReconstructHighFrequency(CriHcaFrame frame)
    {
        var hca = frame.Hca;
        if (hca.HfrGroupCount == 0) return;

        // The last spectral coefficient should always be 0;
        var totalBandCount = Math.Min(hca.TotalBandCount, 127);

        var hfrStartBand = hca.BaseBandCount + hca.StereoBandCount;
        var hfrBandCount = Math.Min(hca.HfrBandCount, totalBandCount - hca.HfrBandCount);

        foreach (var channel in frame.Channels)
        {
            if (channel.Type == ChannelType.StereoSecondary) continue;

            for (int group = 0, band = 0; group < hca.HfrGroupCount; group++)
            {
                for (var i = 0; i < hca.BandsPerHfrGroup && band < hfrBandCount; band++, i++)
                {
                    var highBand = hfrStartBand + band;
                    var lowBand = hfrStartBand - band - 1;
                    var index = channel.HfrScales[group] - channel.ScaleFactors[lowBand] + 64;
                    for (var sf = 0; sf < SubframesPerFrame; sf++)
                    {
                        channel.Spectra[sf][highBand] = ScaleConversionTable[index] * channel.Spectra[sf][lowBand];
                    }
                }
            }
        }
    }

    private static void ApplyIntensityStereo(CriHcaFrame frame)
    {
        if (frame.Hca.StereoBandCount <= 0) return;
        for (var c = 0; c < frame.Channels.Length; c++)
        {
            if (frame.Channels[c].Type != ChannelType.StereoPrimary) continue;
            for (var sf = 0; sf < SubframesPerFrame; sf++)
            {
                var l = frame.Channels[c].Spectra[sf];
                var r = frame.Channels[c + 1].Spectra[sf];
                var ratioL = IntensityRatioTable[frame.Channels[c + 1].Intensity[sf]];
                var ratioR = ratioL - 2.0;
                for (var b = frame.Hca.BaseBandCount; b < frame.Hca.TotalBandCount; b++)
                {
                    r[b] = l[b] * ratioR;
                    l[b] *= ratioL;
                }
            }
        }
    }

    private static void RunImdct(CriHcaFrame frame)
    {
        for (var sf = 0; sf < SubframesPerFrame; sf++)
        {
            foreach (var channel in frame.Channels)
            {
                channel.Mdct.RunImdct(channel.Spectra[sf], channel.PcmFloat[sf]);
            }
        }
    }

    private static void PcmFloatToShort(CriHcaFrame frame, short[][] pcm)
    {
        for (var c = 0; c < frame.Channels.Length; c++)
        {
            for (var sf = 0; sf < SubframesPerFrame; sf++)
            {
                for (var s = 0; s < SamplesPerSubFrame; s++)
                {
                    var sample = (int)(frame.Channels[c].PcmFloat[sf][s] * (short.MaxValue + 1));
                    pcm[c][sf * SamplesPerSubFrame + s] = Helpers.Clamp16(sample);
                }
            }
        }
    }
}