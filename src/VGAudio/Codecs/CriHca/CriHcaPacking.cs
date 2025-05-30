using System;
using System.IO;
using VGAudio.Utilities;
using static VGAudio.Codecs.CriHca.CriHcaConstants;

namespace VGAudio.Codecs.CriHca;

internal static class CriHcaPacking
{
    public static bool UnpackFrame(CriHcaFrame frame, BitReader reader)
    {
        if (!UnpackFrameHeader(frame, reader)) return false;
        ReadSpectralCoefficients(frame, reader);
        return UnpackingWasSuccessful(frame, reader);
    }

    public static void PackFrame(CriHcaFrame frame, Crc16 crc, byte[] outBuffer)
    {
        var writer = new BitWriter(outBuffer);
        writer.Write(0xffff, 16);
        writer.Write(frame.AcceptableNoiseLevel, 9);
        writer.Write(frame.EvaluationBoundary, 7);

        foreach (var channel in frame.Channels)
        {
            WriteScaleFactors(writer, channel);
            if (channel.Type == ChannelType.StereoSecondary)
            {
                for (var i = 0; i < SubframesPerFrame; i++)
                {
                    writer.Write(channel.Intensity[i], 4);
                }
            }
            else if (frame.Hca.HfrGroupCount > 0)
            {
                for (var i = 0; i < frame.Hca.HfrGroupCount; i++)
                {
                    writer.Write(channel.HfrScales[i], 6);
                }
            }
        }

        for (var sf = 0; sf < SubframesPerFrame; sf++)
        {
            foreach (var channel in frame.Channels)
            {
                WriteSpectra(writer, channel, sf);
            }
        }

        writer.AlignPosition(8);
        for (var i = writer.Position / 8; i < frame.Hca.FrameSize - 2; i++)
        {
            writer.Buffer[i] = 0;
        }

        WriteChecksum(writer, crc, outBuffer);
    }

    public static int CalculateResolution(int scaleFactor, int noiseLevel)
    {
        if (scaleFactor == 0) return 0;
        var curvePosition = noiseLevel - 5 * scaleFactor / 2 + 2;
        curvePosition = Helpers.Clamp(curvePosition, 0, 58);
        return CriHcaTables.ScaleToResolutionCurve[curvePosition];
    }

    private static bool UnpackFrameHeader(CriHcaFrame frame, BitReader reader)
    {
        var syncWord = reader.ReadInt(16);
        if (syncWord != 0xffff)
        {
            throw new InvalidDataException("Invalid frame header");
        }

        var athCurve = frame.AthCurve;
        frame.AcceptableNoiseLevel = reader.ReadInt(9);
        frame.EvaluationBoundary = reader.ReadInt(7);

        foreach (var channel in frame.Channels)
        {
            if (!ReadScaleFactors(channel, reader)) return false;

            for (var i = 0; i < frame.EvaluationBoundary; i++)
            {
                channel.Resolution[i] = CalculateResolution(channel.ScaleFactors[i], athCurve[i] + frame.AcceptableNoiseLevel - 1);
            }

            for (var i = frame.EvaluationBoundary; i < channel.CodedScaleFactorCount; i++)
            {
                channel.Resolution[i] = CalculateResolution(channel.ScaleFactors[i], athCurve[i] + frame.AcceptableNoiseLevel);
            }

            if (channel.Type == ChannelType.StereoSecondary)
            {
                ReadIntensity(reader, channel.Intensity);
            }
            else if (frame.Hca.HfrGroupCount > 0)
            {
                ReadHfrScaleFactors(reader, frame.Hca.HfrGroupCount, channel.HfrScales);
            }
        }
        return true;
    }

    private static bool ReadScaleFactors(CriHcaChannel channel, BitReader reader)
    {
        channel.ScaleFactorDeltaBits = reader.ReadInt(3);
        if (channel.ScaleFactorDeltaBits == 0)
        {
            Array.Clear(channel.ScaleFactors, 0, channel.ScaleFactors.Length);
            return true;
        }

        if (channel.ScaleFactorDeltaBits >= 6)
        {
            for (var i = 0; i < channel.CodedScaleFactorCount; i++)
            {
                channel.ScaleFactors[i] = reader.ReadInt(6);
            }
            return true;
        }

        return DeltaDecode(reader, channel.ScaleFactorDeltaBits, 6, channel.CodedScaleFactorCount, channel.ScaleFactors);
    }

    private static void ReadIntensity(BitReader reader, int[] intensity)
    {
        for (var i = 0; i < SubframesPerFrame; i++)
        {
            intensity[i] = reader.ReadInt(4);
        }
    }

    private static void ReadHfrScaleFactors(BitReader reader, int groupCount, int[] hfrScale)
    {
        for (var i = 0; i < groupCount; i++)
        {
            hfrScale[i] = reader.ReadInt(6);
        }
    }

    private static void ReadSpectralCoefficients(CriHcaFrame frame, BitReader reader)
    {
        for (var sf = 0; sf < SubframesPerFrame; sf++)
        {
            foreach (var channel in frame.Channels)
            {
                for (var s = 0; s < channel.CodedScaleFactorCount; s++)
                {
                    var resolution = channel.Resolution[s];
                    int bits = CriHcaTables.QuantizedSpectrumMaxBits[resolution];
                    var code = reader.PeekInt(bits);
                    if (resolution < 8)
                    {
                        bits = CriHcaTables.QuantizedSpectrumBits[resolution][code];
                        channel.QuantizedSpectra[sf][s] = CriHcaTables.QuantizedSpectrumValue[resolution][code];
                    }
                    else
                    {
                        // Read the sign-magnitude value. The low bit is the sign
                        var quantizedCoefficient = code / 2 * (1 - code % 2 * 2);
                        if (quantizedCoefficient == 0)
                        {
                            bits--;
                        }
                        channel.QuantizedSpectra[sf][s] = quantizedCoefficient;
                    }
                    reader.Position += bits;
                }

                Array.Clear(channel.Spectra[sf], channel.CodedScaleFactorCount, 0x80 - channel.CodedScaleFactorCount);
            }
        }
    }

    private static bool DeltaDecode(BitReader reader, int deltaBits, int dataBits, int count, int[] output)
    {
        output[0] = reader.ReadInt(dataBits);
        var maxDelta = 1 << deltaBits - 1;
        var maxValue = (1 << dataBits) - 1;

        for (var i = 1; i < count; i++)
        {
            var delta = reader.ReadOffsetBinary(deltaBits, BitReader.OffsetBias.Positive);

            if (delta < maxDelta)
            {
                var value = output[i - 1] + delta;
                if (value < 0 || value > maxValue)
                {
                    return false;
                }
                output[i] = value;
            }
            else
            {
                output[i] = reader.ReadInt(dataBits);
            }
        }
        return true;
    }

    private static bool UnpackingWasSuccessful(CriHcaFrame frame, BitReader reader)
    {
        // 128 leftover bits after unpacking should be high enough to get rid of false negatives,
        // and low enough that false positives will be uncommon.
        return reader.Remaining >= 16 && reader.Remaining <= 128
               || FrameEmpty(frame)
               || frame.AcceptableNoiseLevel == 0 && reader.Remaining >= 16;
    }

    private static bool FrameEmpty(CriHcaFrame frame)
    {
        if (frame.AcceptableNoiseLevel > 0) return false;

        // If all the scale factors are 0, the frame is empty
        foreach (var channel in frame.Channels)
        {
            if (channel.ScaleFactorDeltaBits > 0)
            {
                return false;
            }
        }
        return true;
    }

    private static void WriteChecksum(BitWriter writer, Crc16 crc, Span<byte> hcaBuffer)
    {
        writer.Position = writer.LengthBits - 16;
        var crc16 = crc.Compute(hcaBuffer[..^2]);
        writer.Write(crc16, 16);
    }

    private static void WriteSpectra(BitWriter writer, CriHcaChannel channel, int subFrame)
    {
        for (var i = 0; i < channel.CodedScaleFactorCount; i++)
        {
            var resolution = channel.Resolution[i];
            var quantizedSpectra = channel.QuantizedSpectra[subFrame][i];
            if (resolution == 0) continue;
            if (resolution < 8)
            {
                int bits = CriHcaTables.QuantizeSpectrumBits[resolution][quantizedSpectra + 8];
                writer.Write(CriHcaTables.QuantizeSpectrumValue[resolution][quantizedSpectra + 8], bits);
            }
            else if (resolution < 16)
            {
                var bits = CriHcaTables.QuantizedSpectrumMaxBits[resolution] - 1;
                writer.Write(Math.Abs(quantizedSpectra), bits);
                if (quantizedSpectra != 0)
                {
                    writer.Write(quantizedSpectra > 0 ? 0 : 1, 1);
                }
            }
        }
    }

    private static void WriteScaleFactors(BitWriter writer, CriHcaChannel channel)
    {
        var deltaBits = channel.ScaleFactorDeltaBits;
        var scales = channel.ScaleFactors;
        writer.Write(deltaBits, 3);
        if (deltaBits == 0) return;

        if (deltaBits == 6)
        {
            for (var i = 0; i < channel.CodedScaleFactorCount; i++)
            {
                writer.Write(scales[i], 6);
            }
            return;
        }

        writer.Write(scales[0], 6);
        var maxDelta = (1 << deltaBits - 1) - 1;
        var escapeValue = (1 << deltaBits) - 1;

        for (var i = 1; i < channel.CodedScaleFactorCount; i++)
        {
            var delta = scales[i] - scales[i - 1];
            if (Math.Abs(delta) > maxDelta)
            {
                writer.Write(escapeValue, deltaBits);
                writer.Write(scales[i], 6);
            }
            else
            {
                writer.Write(maxDelta + delta, deltaBits);
            }
        }
    }
}