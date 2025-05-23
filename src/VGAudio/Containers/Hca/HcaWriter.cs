﻿using System.IO;
using System.Text;
using VGAudio.Codecs.CriHca;
using VGAudio.Formats;
using VGAudio.Formats.CriHca;
using VGAudio.Utilities;
using static VGAudio.Utilities.Helpers;

namespace VGAudio.Containers.Hca;

public class HcaWriter : AudioWriter<HcaWriter, HcaConfiguration>
{
    private HcaInfo Hca { get; set; }
    private byte[][] AudioData { get; set; }
    private short Version => 0x0200;

    private Crc16 Crc { get; } = new(0x8005);

    private int HeaderSize => Hca.HeaderSize;

    protected override int FileSize => HeaderSize + Hca.FrameSize * Hca.FrameCount;

    protected override void SetupWriter(AudioData audio)
    {
        var encodingConfig = new CriHcaParameters
        {
            Progress = Configuration.Progress,
            Bitrate = Configuration.Bitrate,
            LimitBitrate = Configuration.LimitBitrate
        };
        if (Configuration.Quality != CriHcaQuality.NotSet)
        {
            encodingConfig.Quality = Configuration.Quality;
        }

        var hcaFormat = audio.GetFormat<CriHcaFormat>(encodingConfig);
        Hca = hcaFormat.Hca;
        AudioData = hcaFormat.AudioData;

        if (Configuration.EncryptionKey != null)
        {
            CriHcaEncryption.Crypt(Hca, AudioData, Configuration.EncryptionKey, false);

            Hca.EncryptionType = Configuration.EncryptionKey.KeyType;
        }
    }

    protected override void WriteStream(Stream stream)
    {
        using var writer = GetBinaryWriter(stream, Endianness.BigEndian);
        WriteHeader(writer);
        WriteData(writer);
    }

    private void WriteHeader(BinaryWriter writer)
    {
        WriteHcaChunk(writer);
        WriteFmtChunk(writer);
        WriteCompChunk(writer);
        WriteLoopChunk(writer);
        WriteCiphChunk(writer);
        WriteRvaChunk(writer);

        if (string.IsNullOrWhiteSpace(Hca.Comment))
        {
            WritePadChunk(writer);
        }
        else
        {
            WriteCommChunk(writer);
        }

        writer.BaseStream.Position = 0;
        var header = new byte[HeaderSize - 2];
        writer.BaseStream.ReadExactly(header, 0, header.Length);
        var crc16 = Crc.Compute(header, header.Length);
        writer.Write(crc16);
    }

    private void WriteHcaChunk(BinaryWriter writer)
    {
        WriteChunkId(writer, "HCA\0");
        writer.Write(Version);
        writer.Write((short)HeaderSize);
    }

    private void WriteFmtChunk(BinaryWriter writer)
    {
        WriteChunkId(writer, "fmt\0");
        writer.Write((byte)Hca.ChannelCount);

        // Sample Rate is 24-bit
        writer.Write((byte)(Hca.SampleRate >> 16));
        writer.Write((short)Hca.SampleRate);

        writer.Write(Hca.FrameCount);
        writer.Write((ushort)Hca.InsertedSamples);
        writer.Write((ushort)Hca.AppendedSamples);
    }

    private void WriteCompChunk(BinaryWriter writer)
    {
        WriteChunkId(writer, "comp");
        writer.Write((short)Hca.FrameSize);
        writer.Write((byte)Hca.MinResolution);
        writer.Write((byte)Hca.MaxResolution);
        writer.Write((byte)Hca.TrackCount);
        writer.Write((byte)Hca.ChannelConfig);
        writer.Write((byte)Hca.TotalBandCount);
        writer.Write((byte)Hca.BaseBandCount);
        writer.Write((byte)Hca.StereoBandCount);
        writer.Write((byte)Hca.BandsPerHfrGroup);
        writer.Write((short)0);
    }

    private void WriteLoopChunk(BinaryWriter writer)
    {
        if (!Hca.Looping) return;

        WriteChunkId(writer, "loop");
        writer.Write(Hca.LoopStartFrame);
        writer.Write(Hca.LoopEndFrame);
        writer.Write((short)Hca.PreLoopSamples);
        writer.Write((short)Hca.PostLoopSamples);
    }

    private void WriteCiphChunk(BinaryWriter writer)
    {
        WriteChunkId(writer, "ciph");
        writer.Write((short)Hca.EncryptionType);
    }

    private void WriteRvaChunk(BinaryWriter writer)
    {
        var volume = Hca.Volume;
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (volume != 1)
        {
            WriteChunkId(writer, "rva\0");
            writer.Write(volume);
        }
    }

    private void WriteCommChunk(BinaryWriter writer)
    {
        WriteChunkId(writer, "comm\0");
        writer.WriteUtf8Z(Hca.Comment);
    }

    private void WritePadChunk(BinaryWriter writer)
    {
        WriteChunkId(writer, "pad");
    }

    private void WriteChunkId(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);

        if (Configuration.EncryptionKey != null)
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0) bytes[i] |= 0x80;
            }
        }

        writer.Write(bytes);
    }

    private void WriteData(BinaryWriter writer)
    {
        for (var i = 0; i < Hca.FrameCount; i++)
        {
            writer.Write(AudioData[i], 0, Hca.FrameSize);
        }
    }
}