﻿using System;
using System.Collections.Generic;
using System.IO;
using VGAudio.Utilities;
using static VGAudio.Codecs.CriHca.CriHcaConstants;
using static VGAudio.Codecs.CriHca.CriHcaPacking;
using static VGAudio.Utilities.Helpers;

namespace VGAudio.Codecs.CriHca;

public class CriHcaEncoder
{
    private CriHcaEncoder()
    {
    }

    public HcaInfo Hca { get; private set; }
    public CriHcaQuality Quality { get; private set; }
    public int Bitrate { get; private set; }
    public int CutoffFrequency { get; private set; }

    /// <summary>
    ///     The number of buffered frames waiting to be read.
    ///     All buffered frames must be read before calling <see cref="Encode" /> again.
    /// </summary>
    public int PendingFrameCount => HcaOutputBuffer.Count;

    /// <summary>
    ///     The size, in bytes, of one frame of HCA audio data.
    /// </summary>
    public int FrameSize => Hca.FrameSize;

    private CriHcaChannel[] Channels { get; set; }
    private CriHcaFrame Frame { get; set; }
    private Crc16 Crc { get; } = new(0x8005);

    private short[][] PcmBuffer { get; set; }
    private int BufferPosition { get; set; }
    private int BufferRemaining => SamplesPerFrame - BufferPosition;
    private int BufferPreSamples { get; set; }
    private int SamplesProcessed { get; set; }
    public int FramesProcessed { get; private set; }
    private int PostSamples { get; set; }
    private short[][] PostAudio { get; set; }

    private Queue<byte[]> HcaOutputBuffer { get; set; }

    /// <summary>
    ///     Creates and initializes a new <see cref="CriHcaEncoder" />. The encoder
    ///     will be ready to accept PCM audio via <see cref="Encode" />.
    /// </summary>
    /// <param name="config">The configuration to be used when creating the HCA file.</param>
    public static CriHcaEncoder InitializeNew(CriHcaParameters config)
    {
        var encoder = new CriHcaEncoder();
        encoder.Initialize(config);
        return encoder;
    }

    /// <summary>
    ///     Initializes this <see cref="CriHcaEncoder" />. Any preexisting state is reset, and the encoder
    ///     will be ready to accept PCM audio via <see cref="Encode" />.
    /// </summary>
    /// <param name="config">The configuration to be used when creating the HCA file.</param>
    public void Initialize(CriHcaParameters config)
    {
        if (config.ChannelCount > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(config.ChannelCount), "HCA channel count must be 8 or below");
        }

        CutoffFrequency = config.SampleRate / 2;
        Quality = config.Quality;
        PostSamples = 128;

        Hca = new HcaInfo
        {
            ChannelCount = config.ChannelCount,
            TrackCount = 1,
            SampleCount = config.SampleCount,
            SampleRate = config.SampleRate,
            MinResolution = 1,
            MaxResolution = 15,
            InsertedSamples = SamplesPerSubFrame
        };

        Bitrate = CalculateBitrate(Hca, Quality, config.Bitrate, config.LimitBitrate);
        CalculateBandCounts(Hca, Bitrate, CutoffFrequency);
        Hca.CalculateHfrValues();
        SetChannelConfiguration(Hca);

        var inputSampleCount = Hca.SampleCount;

        if (config.Looping)
        {
            Hca.Looping = true;
            Hca.SampleCount = Math.Min(config.LoopEnd, config.SampleCount);
            Hca.InsertedSamples += GetNextMultiple(config.LoopStart, SamplesPerFrame) - config.LoopStart;
            CalculateLoopInfo(Hca, config.LoopStart, config.LoopEnd);
            inputSampleCount = Math.Min(GetNextMultiple(Hca.SampleCount, SamplesPerSubFrame), config.SampleCount);
            inputSampleCount += SamplesPerSubFrame * 2;
            PostSamples = inputSampleCount - Hca.SampleCount;
        }

        CalculateHeaderSize(Hca);

        var totalSamples = inputSampleCount + Hca.InsertedSamples;

        Hca.FrameCount = totalSamples.DivideByRoundUp(SamplesPerFrame);
        Hca.AppendedSamples = Hca.FrameCount * SamplesPerFrame - Hca.InsertedSamples - inputSampleCount;

        Frame = new CriHcaFrame(Hca);
        Channels = Frame.Channels;
        PcmBuffer = CreateJaggedArray<short[][]>(Hca.ChannelCount, SamplesPerFrame);
        PostAudio = CreateJaggedArray<short[][]>(Hca.ChannelCount, PostSamples);
        HcaOutputBuffer = new Queue<byte[]>();
        BufferPreSamples = Hca.InsertedSamples - 128;
    }

    /// <summary>
    ///     Encodes one frame of PCM audio into the HCA format.
    /// </summary>
    /// <param name="pcm">
    ///     The PCM audio to encode. The array must be a jagged array
    ///     of the size [ChannelCount][1024] or larger.
    /// </param>
    /// <param name="hcaOut">
    ///     The buffer that the encoded HCA frame will be placed in.
    ///     Must be at least <see cref="FrameSize" /> bytes long.
    /// </param>
    /// <returns>
    ///     The number of HCA frames that were output by the encoder.
    ///     The first frame is output to <paramref name="hcaOut" />. Any additional frames must be retrieved
    ///     by calling <see cref="GetPendingFrame" /> before <see cref="Encode" /> can be called again.
    /// </returns>
    public int Encode(short[][] pcm, byte[] hcaOut)
    {
        if (FramesProcessed >= Hca.FrameCount)
        {
            throw new InvalidOperationException("All audio frames have already been output by the encoder");
        }
        var framesOutput = 0;
        var pcmPosition = 0;

        if (BufferPreSamples > 0)
        {
            framesOutput = EncodePreAudio(pcm, hcaOut, framesOutput);
        }

        if (Hca.Looping && Hca.LoopStartSample + PostSamples >= SamplesProcessed && Hca.LoopStartSample < SamplesProcessed + SamplesPerFrame)
        {
            SaveLoopAudio(pcm);
        }

        while (SamplesPerFrame - pcmPosition > 0 && Hca.SampleCount > SamplesProcessed)
        {
            framesOutput = EncodeMainAudio(pcm, hcaOut, framesOutput, ref pcmPosition);
        }

        if (Hca.SampleCount == SamplesProcessed)
        {
            framesOutput = EncodePostAudio(pcm, hcaOut, framesOutput);
        }

        return framesOutput;
    }

    /// <summary>
    ///     Returns the next HCA frame awaiting output.
    /// </summary>
    /// <returns>Byte array containing the HCA frame data. The caller is given ownership of this array.</returns>
    /// <exception cref="InvalidOperationException">Thrown when there are no frames awaiting output.</exception>
    public byte[] GetPendingFrame()
    {
        if (PendingFrameCount == 0) throw new InvalidOperationException("There are no pending frames");

        return HcaOutputBuffer.Dequeue();
    }

    private int EncodePreAudio(short[][] pcm, byte[] hcaOut, int framesOutput)
    {
        while (BufferPreSamples > SamplesPerFrame)
        {
            BufferPosition = SamplesPerFrame;
            framesOutput = OutputFrame(framesOutput, hcaOut);
            BufferPreSamples -= SamplesPerFrame;
        }

        for (var j = 0; j < BufferPreSamples; j++)
        {
            for (var i = 0; i < pcm.Length; i++)
            {
                PcmBuffer[i][j] = pcm[i][0];
            }
        }

        BufferPosition = BufferPreSamples;
        BufferPreSamples = 0;
        return framesOutput;
    }

    private int EncodeMainAudio(short[][] pcm, byte[] hcaOut, int framesOutput, ref int pcmPosition)
    {
        var toCopy = Math.Min(BufferRemaining, SamplesPerFrame - pcmPosition);
        toCopy = Math.Min(toCopy, Hca.SampleCount - SamplesProcessed);

        for (var i = 0; i < pcm.Length; i++)
        {
            Array.Copy(pcm[i], pcmPosition, PcmBuffer[i], BufferPosition, toCopy);
        }
        BufferPosition += toCopy;
        SamplesProcessed += toCopy;
        pcmPosition += toCopy;

        framesOutput = OutputFrame(framesOutput, hcaOut);
        return framesOutput;
    }

    private int EncodePostAudio(short[][] pcm, byte[] hcaOut, int framesOutput)
    {
        var postPos = 0;
        var remaining = PostSamples;

        // Add audio from the loop start
        while (postPos < remaining)
        {
            var toCopy = Math.Min(BufferRemaining, remaining - postPos);
            for (var i = 0; i < pcm.Length; i++)
            {
                Array.Copy(PostAudio[i], postPos, PcmBuffer[i], BufferPosition, toCopy);
            }

            BufferPosition += toCopy;
            postPos += toCopy;

            framesOutput = OutputFrame(framesOutput, hcaOut);
        }

        // Output any remaining frames
        while (FramesProcessed < Hca.FrameCount)
        {
            for (var i = 0; i < pcm.Length; i++)
            {
                Array.Clear(PcmBuffer[i], BufferPosition, BufferRemaining);
            }
            BufferPosition = SamplesPerFrame;

            framesOutput = OutputFrame(framesOutput, hcaOut);
        }

        return framesOutput;
    }

    private void SaveLoopAudio(short[][] pcm)
    {
        var startPos = Math.Max(Hca.LoopStartSample - SamplesProcessed, 0);
        var loopPos = Math.Max(SamplesProcessed - Hca.LoopStartSample, 0);
        var endPos = Math.Min(Hca.LoopStartSample - SamplesProcessed + PostSamples, SamplesPerFrame);
        var length = endPos - startPos;
        for (var i = 0; i < pcm.Length; i++)
        {
            Array.Copy(pcm[i], startPos, PostAudio[i], loopPos, length);
        }
    }

    private int OutputFrame(int framesOutput, byte[] hcaOut)
    {
        if (BufferRemaining != 0) return framesOutput;

        var hca = framesOutput == 0 ? hcaOut : new byte[Hca.FrameSize];
        EncodeFrame(PcmBuffer, hca);
        if (framesOutput > 0)
        {
            HcaOutputBuffer.Enqueue(hca);
        }
        BufferPosition = 0;
        FramesProcessed++;
        return framesOutput + 1;
    }

    private void EncodeFrame(short[][] pcm, byte[] hcaOut)
    {
        PcmToFloat(pcm, Channels);
        RunMdct(Channels);
        EncodeIntensityStereo(Frame);
        CalculateScaleFactors(Channels);
        ScaleSpectra(Channels);
        CalculateHfrGroupAverages(Frame);
        CalculateHfrScale(Frame);
        CalculateFrameHeaderLength(Frame);
        CalculateNoiseLevel(Frame);
        CalculateEvaluationBoundary(Frame);
        CalculateFrameResolutions(Frame);
        QuantizeSpectra(Channels);
        PackFrame(Frame, Crc, hcaOut);
    }

    private int CalculateBitrate(HcaInfo hca, CriHcaQuality quality, int bitrate, bool limitBitrate)
    {
        var pcmBitrate = Hca.SampleRate * Hca.ChannelCount * 16;
        var maxBitrate = pcmBitrate / 4;
        var minBitrate = 0;

        var compressionRatio = 6;
        switch (quality)
        {
            case CriHcaQuality.Highest:
                compressionRatio = 4;
                break;
            case CriHcaQuality.High:
                compressionRatio = 6;
                break;
            case CriHcaQuality.Middle:
                compressionRatio = 8;
                break;
            case CriHcaQuality.Low:
                compressionRatio = hca.ChannelCount == 1 ? 10 : 12;
                break;
            case CriHcaQuality.Lowest:
                compressionRatio = hca.ChannelCount == 1 ? 12 : 16;
                break;
        }

        bitrate = bitrate != 0 ? bitrate : pcmBitrate / compressionRatio;

        if (limitBitrate)
        {
            minBitrate = Math.Min(
                hca.ChannelCount == 1 ? 42666 : 32000 * hca.ChannelCount,
                pcmBitrate / 6);
        }

        return Clamp(bitrate, minBitrate, maxBitrate);
    }

    private static void CalculateBandCounts(HcaInfo hca, int bitrate, int cutoffFreq)
    {
        hca.FrameSize = bitrate * 1024 / hca.SampleRate / 8;
        var numGroups = 0;
        var pcmBitrate = hca.SampleRate * hca.ChannelCount * 16;
        int hfrRatio; // HFR is used at bitrates below (pcmBitrate / hfrRatio)
        int cutoffRatio; // The cutoff frequency is lowered at bitrates below (pcmBitrate / cutoffRatio)

        if (hca.ChannelCount <= 1 || pcmBitrate / bitrate <= 6)
        {
            hfrRatio = 6;
            cutoffRatio = 12;
        }
        else
        {
            hfrRatio = 8;
            cutoffRatio = 16;
        }

        if (bitrate < pcmBitrate / cutoffRatio)
        {
            cutoffFreq = Math.Min(cutoffFreq, cutoffRatio * bitrate / (32 * hca.ChannelCount));
        }

        var totalBandCount = (int)Math.Round(cutoffFreq * 256.0 / hca.SampleRate);

        var hfrStartBand = (int)Math.Min(totalBandCount, Math.Round(hfrRatio * bitrate * 128.0 / pcmBitrate));
        var stereoStartBand = hfrRatio == 6 ? hfrStartBand : (hfrStartBand + 1) / 2;

        var hfrBandCount = totalBandCount - hfrStartBand;
        var bandsPerGroup = hfrBandCount.DivideByRoundUp(8);

        if (bandsPerGroup > 0)
        {
            numGroups = hfrBandCount.DivideByRoundUp(bandsPerGroup);
        }

        hca.TotalBandCount = totalBandCount;
        hca.BaseBandCount = stereoStartBand;
        hca.StereoBandCount = hfrStartBand - stereoStartBand;
        hca.HfrGroupCount = numGroups;
        hca.BandsPerHfrGroup = bandsPerGroup;
    }

    private static void SetChannelConfiguration(HcaInfo hca, int channelConfig = -1)
    {
        var channelsPerTrack = hca.ChannelCount / hca.TrackCount;
        if (channelConfig == -1) channelConfig = CriHcaTables.DefaultChannelMapping[channelsPerTrack];

        if (CriHcaTables.ValidChannelMappings[channelsPerTrack - 1][channelConfig] != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(channelConfig), "Channel mapping is not valid.");
        }

        hca.ChannelConfig = channelConfig;
    }

    private static void CalculateLoopInfo(HcaInfo hca, int loopStart, int loopEnd)
    {
        loopStart += hca.InsertedSamples;
        loopEnd += hca.InsertedSamples;

        hca.LoopStartFrame = loopStart / SamplesPerFrame;
        hca.PreLoopSamples = loopStart % SamplesPerFrame;
        hca.LoopEndFrame = loopEnd / SamplesPerFrame;
        hca.PostLoopSamples = SamplesPerFrame - loopEnd % SamplesPerFrame;

        if (hca.PostLoopSamples == SamplesPerFrame)
        {
            hca.LoopEndFrame--;
            hca.PostLoopSamples = 0;
        }
    }

    private static void CalculateHeaderSize(HcaInfo hca)
    {
        const int baseHeaderSize = 96;
        const int baseHeaderAlignment = 32;
        const int loopFrameAlignment = 2048;

        hca.HeaderSize = GetNextMultiple(baseHeaderSize + hca.CommentLength, baseHeaderAlignment);
        if (hca.Looping)
        {
            var loopFrameOffset = hca.HeaderSize + hca.FrameSize * hca.LoopStartFrame;
            var paddingBytes = GetNextMultiple(loopFrameOffset, loopFrameAlignment) - loopFrameOffset;
            var paddingFrames = paddingBytes / hca.FrameSize;

            hca.InsertedSamples += paddingFrames * SamplesPerFrame;
            hca.LoopStartFrame += paddingFrames;
            hca.LoopEndFrame += paddingFrames;
            hca.HeaderSize += paddingBytes % hca.FrameSize;
        }
    }

    private static void QuantizeSpectra(CriHcaChannel[] channels)
    {
        foreach (var channel in channels)
        {
            for (var i = 0; i < channel.CodedScaleFactorCount; i++)
            {
                var scaled = channel.ScaledSpectra[i];
                var resolution = channel.Resolution[i];
                var stepSizeInv = CriHcaTables.QuantizerInverseStepSize[resolution];
                var shiftUp = stepSizeInv + 1;
                var shiftDown = (int)(stepSizeInv + 0.5);

                for (var sf = 0; sf < scaled.Length; sf++)
                {
                    var quantizedSpectra = (int)(scaled[sf] * stepSizeInv + shiftUp) - shiftDown;
                    channel.QuantizedSpectra[sf][i] = quantizedSpectra;
                }
            }
        }
    }

    private static void CalculateFrameResolutions(CriHcaFrame frame)
    {
        foreach (var channel in frame.Channels)
        {
            for (var i = 0; i < frame.EvaluationBoundary; i++)
            {
                channel.Resolution[i] = CalculateResolution(channel.ScaleFactors[i], frame.AcceptableNoiseLevel - 1);
            }
            for (var i = frame.EvaluationBoundary; i < channel.CodedScaleFactorCount; i++)
            {
                channel.Resolution[i] = CalculateResolution(channel.ScaleFactors[i], frame.AcceptableNoiseLevel);
            }
            Array.Clear(channel.Resolution, channel.CodedScaleFactorCount, channel.Resolution.Length - channel.CodedScaleFactorCount);
        }
    }

    private static void CalculateNoiseLevel(CriHcaFrame frame)
    {
        var highestBand = frame.Hca.BaseBandCount + frame.Hca.StereoBandCount - 1;
        var availableBits = frame.Hca.FrameSize * 8;
        var maxLevel = 255;
        var minLevel = 0;
        var level = BinarySearchLevel(frame.Channels, availableBits, minLevel, maxLevel);

        // If there aren't enough available bits, remove bands until there are.
        while (level < 0)
        {
            highestBand -= 2;
            if (highestBand < 0)
            {
                throw new InvalidDataException("Bitrate is set too low.");
            }

            foreach (var channel in frame.Channels)
            {
                channel.ScaleFactors[highestBand + 1] = 0;
                channel.ScaleFactors[highestBand + 2] = 0;
            }

            CalculateFrameHeaderLength(frame);
            level = BinarySearchLevel(frame.Channels, availableBits, minLevel, maxLevel);
        }

        frame.AcceptableNoiseLevel = level;
    }

    private static void CalculateEvaluationBoundary(CriHcaFrame frame)
    {
        if (frame.AcceptableNoiseLevel == 0)
        {
            frame.EvaluationBoundary = 0;
            return;
        }

        var availableBits = frame.Hca.FrameSize * 8;
        var maxLevel = 127;
        var minLevel = 0;
        var level = BinarySearchBoundary(frame.Channels, availableBits, frame.AcceptableNoiseLevel, minLevel, maxLevel);
        frame.EvaluationBoundary = level >= 0 ? level : throw new NotImplementedException();
    }

    private static int BinarySearchLevel(CriHcaChannel[] channels, int availableBits, int low, int high)
    {
        var max = high;
        var midValue = 0;

        while (low != high)
        {
            var mid = (low + high) / 2;
            midValue = CalculateUsedBits(channels, mid, 0);

            if (midValue > availableBits)
            {
                low = mid + 1;
            }
            else if (midValue <= availableBits)
            {
                high = mid;
            }
        }

        return low == max && midValue > availableBits ? -1 : low;
    }

    private static int BinarySearchBoundary(CriHcaChannel[] channels, int availableBits, int noiseLevel, int low, int high)
    {
        var max = high;

        while (Math.Abs(high - low) > 1)
        {
            var mid = (low + high) / 2;
            var midValue = CalculateUsedBits(channels, noiseLevel, mid);

            if (availableBits < midValue)
            {
                high = mid - 1;
            }
            else if (availableBits >= midValue)
            {
                low = mid;
            }
        }

        if (low == high)
        {
            return low < max ? low : -1;
        }

        var hiValue = CalculateUsedBits(channels, noiseLevel, high);

        return hiValue > availableBits ? low : high;
    }

    private static int CalculateUsedBits(CriHcaChannel[] channels, int noiseLevel, int evalBoundary)
    {
        var length = 16 + 16 + 16; // Sync word, noise level and checksum

        foreach (var channel in channels)
        {
            length += channel.HeaderLengthBits;
            for (var i = 0; i < channel.CodedScaleFactorCount; i++)
            {
                var noise = i < evalBoundary ? noiseLevel - 1 : noiseLevel;
                var resolution = CalculateResolution(channel.ScaleFactors[i], noise);

                if (resolution >= 8)
                {
                    // To determine the bit count, we only need to know if the value
                    // falls in the quantizer's dead zone.
                    var bits = CriHcaTables.QuantizedSpectrumMaxBits[resolution] - 1;
                    var deadZone = CriHcaTables.QuantizerDeadZone[resolution];
                    foreach (var scaledSpectra in channel.ScaledSpectra[i])
                    {
                        length += bits;
                        if (Math.Abs(scaledSpectra) >= deadZone) length++;
                    }
                }
                else
                {
                    // To determine the bit count, we need to quantize the value and check
                    // the number of bits its prefix code uses.
                    // Compute the floor function by shifting the numbers to be above 0,
                    // truncating them, then shifting them back down to their original range.
                    var stepSizeInv = CriHcaTables.QuantizerInverseStepSize[resolution];
                    var shiftUp = stepSizeInv + 1;
                    var shiftDown = (int)(stepSizeInv + 0.5 - 8);
                    foreach (var scaledSpectra in channel.ScaledSpectra[i])
                    {
                        var quantizedSpectra = (int)(scaledSpectra * stepSizeInv + shiftUp) - shiftDown;
                        length += CriHcaTables.QuantizeSpectrumBits[resolution][quantizedSpectra];
                    }
                }
            }
        }

        return length;
    }

    private static void CalculateFrameHeaderLength(CriHcaFrame frame)
    {
        foreach (var channel in frame.Channels)
        {
            CalculateOptimalDeltaLength(channel);
            if (channel.Type == ChannelType.StereoSecondary) channel.HeaderLengthBits += 32;
            else if (frame.Hca.HfrGroupCount > 0) channel.HeaderLengthBits += 6 * frame.Hca.HfrGroupCount;
        }
    }

    private static void CalculateOptimalDeltaLength(CriHcaChannel channel)
    {
        var emptyChannel = true;
        for (var i = 0; i < channel.CodedScaleFactorCount; i++)
        {
            if (channel.ScaleFactors[i] != 0)
            {
                emptyChannel = false;
                break;
            }
        }

        if (emptyChannel)
        {
            channel.HeaderLengthBits = 3;
            channel.ScaleFactorDeltaBits = 0;
            return;
        }

        var minDeltaBits = 6;
        var minLength = 3 + 6 * channel.CodedScaleFactorCount;

        for (var deltaBits = 1; deltaBits < 6; deltaBits++)
        {
            var maxDelta = (1 << deltaBits - 1) - 1;
            var length = 3 + 6;
            for (var band = 1; band < channel.CodedScaleFactorCount; band++)
            {
                var delta = channel.ScaleFactors[band] - channel.ScaleFactors[band - 1];
                length += Math.Abs(delta) > maxDelta ? deltaBits + 6 : deltaBits;
            }
            if (length < minLength)
            {
                minLength = length;
                minDeltaBits = deltaBits;
            }
        }

        channel.HeaderLengthBits = minLength;
        channel.ScaleFactorDeltaBits = minDeltaBits;
    }

    private static void ScaleSpectra(CriHcaChannel[] channels)
    {
        foreach (var channel in channels)
        {
            for (var b = 0; b < channel.CodedScaleFactorCount; b++)
            {
                var scaledSpectra = channel.ScaledSpectra[b];
                var scaleFactor = channel.ScaleFactors[b];
                for (var sf = 0; sf < scaledSpectra.Length; sf++)
                {
                    var coeff = channel.Spectra[sf][b];
                    scaledSpectra[sf] = scaleFactor == 0 ? 0 : Clamp(coeff * CriHcaTables.QuantizerScalingTable[scaleFactor], -0.999999999999, 0.999999999999);
                    // Precision loss when rounding affects the floating point values just below 1.
                    // We avoid this by having clamp values that are about 9000 steps below 1.0.
                    // The number is slightly arbitrary. I just picked one that's far enough from 1
                    // to not cause any issues.
                }
            }
        }
    }

    private static void CalculateScaleFactors(CriHcaChannel[] channels)
    {
        foreach (var channel in channels)
        {
            for (var b = 0; b < channel.CodedScaleFactorCount; b++)
            {
                double max = 0;
                for (var sf = 0; sf < SubframesPerFrame; sf++)
                {
                    var coeff = Math.Abs(channel.Spectra[sf][b]);
                    max = Math.Max(coeff, max);
                }
                channel.ScaleFactors[b] = FindScaleFactor(max);
            }
            Array.Clear(channel.ScaleFactors, channel.CodedScaleFactorCount, channel.ScaleFactors.Length - channel.CodedScaleFactorCount);
        }
    }

    private static int FindScaleFactor(double value)
    {
        var sf = CriHcaTables.DequantizerScalingTable;
        uint low = 0;
        uint high = 63;
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (sf[mid] <= value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }
        return (int)low;
    }

    private static void EncodeIntensityStereo(CriHcaFrame frame)
    {
        if (frame.Hca.StereoBandCount <= 0) return;

        for (var c = 0; c < frame.Channels.Length; c++)
        {
            if (frame.Channels[c].Type != ChannelType.StereoPrimary) continue;

            for (var sf = 0; sf < SubframesPerFrame; sf++)
            {
                var l = frame.Channels[c].Spectra[sf];
                var r = frame.Channels[c + 1].Spectra[sf];

                double energyL = 0;
                double energyR = 0;
                double energyTotal = 0;

                for (var b = frame.Hca.BaseBandCount; b < frame.Hca.TotalBandCount; b++)
                {
                    energyL += Math.Abs(l[b]);
                    energyR += Math.Abs(r[b]);
                    energyTotal += Math.Abs(l[b] + r[b]);
                }
                energyTotal *= 2;

                var energyLr = energyR + energyL;
                var storedValue = 2 * energyL / energyLr;
                var energyRatio = energyLr / energyTotal;
                energyRatio = Clamp(energyRatio, 0.5, Math.Sqrt(2) / 2);

                var quantized = 1;
                if (energyR > 0 || energyL > 0)
                {
                    while (quantized < 13 && CriHcaTables.IntensityRatioBoundsTable[quantized] >= storedValue)
                    {
                        quantized++;
                    }
                }
                else
                {
                    quantized = 0;
                    energyRatio = 1;
                }

                frame.Channels[c + 1].Intensity[sf] = quantized;

                for (var b = frame.Hca.BaseBandCount; b < frame.Hca.TotalBandCount; b++)
                {
                    l[b] = (l[b] + r[b]) * energyRatio;
                    r[b] = 0;
                }
            }
        }
    }

    private static void CalculateHfrGroupAverages(CriHcaFrame frame)
    {
        var hca = frame.Hca;
        if (hca.HfrGroupCount == 0) return;

        var hfrStartBand = hca.StereoBandCount + hca.BaseBandCount;
        foreach (var channel in frame.Channels)
        {
            if (channel.Type == ChannelType.StereoSecondary) continue;

            for (int group = 0, band = hfrStartBand; group < hca.HfrGroupCount; group++)
            {
                var sum = 0.0;
                var count = 0;

                for (var i = 0; i < hca.BandsPerHfrGroup && band < SamplesPerSubFrame; band++, i++)
                {
                    for (var subframe = 0; subframe < SubframesPerFrame; subframe++)
                    {
                        sum += Math.Abs(channel.Spectra[subframe][band]);
                    }
                    count += SubframesPerFrame;
                }

                channel.HfrGroupAverageSpectra[group] = sum / count;
            }
        }
    }

    private static void CalculateHfrScale(CriHcaFrame frame)
    {
        var hca = frame.Hca;
        if (hca.HfrGroupCount == 0) return;

        var hfrStartBand = hca.StereoBandCount + hca.BaseBandCount;
        var hfrBandCount = Math.Min(hca.HfrBandCount, hca.TotalBandCount - hca.HfrBandCount);

        foreach (var channel in frame.Channels)
        {
            if (channel.Type == ChannelType.StereoSecondary) continue;

            var groupSpectra = channel.HfrGroupAverageSpectra;

            for (int group = 0, band = 0; group < hca.HfrGroupCount; group++)
            {
                var sum = 0.0;
                var count = 0;

                for (var i = 0; i < hca.BandsPerHfrGroup && band < hfrBandCount; band++, i++)
                {
                    for (var subframe = 0; subframe < SubframesPerFrame; subframe++)
                    {
                        sum += Math.Abs(channel.ScaledSpectra[hfrStartBand - band - 1][subframe]);
                    }
                    count += SubframesPerFrame;
                }

                var averageSpectra = sum / count;
                if (averageSpectra > 0.0)
                {
                    groupSpectra[group] *= Math.Min(1.0 / averageSpectra, Math.Sqrt(2));
                }

                channel.HfrScales[group] = FindScaleFactor(groupSpectra[group]);
            }
        }
    }

    private static void RunMdct(CriHcaChannel[] channels)
    {
        foreach (var channel in channels)
        {
            for (var sf = 0; sf < SubframesPerFrame; sf++)
            {
                channel.Mdct.RunMdct(channel.PcmFloat[sf], channel.Spectra[sf]);
            }
        }
    }

    private static void PcmToFloat(short[][] pcm, CriHcaChannel[] channels)
    {
        for (var c = 0; c < channels.Length; c++)
        {
            var pcmIdx = 0;
            for (var sf = 0; sf < SubframesPerFrame; sf++)
            {
                for (var i = 0; i < SamplesPerSubFrame; i++)
                {
                    channels[c].PcmFloat[sf][i] = pcm[c][pcmIdx++] * (1.0 / 32768.0);
                }
            }
        }
    }
}