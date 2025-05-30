using System;
using VGAudio.Utilities;

namespace VGAudio.Codecs.CriHca;

public static partial class CriHcaEncryption
{
    private const int FRAMES_TO_TEST = 10;

    private static readonly Crc16 Crc = new(0x8005);

    public static void Crypt(HcaInfo hca, byte[][] audio, CriHcaKey key, bool doDecrypt)
    {
        foreach (var frame in audio)
        {
            CryptFrame(hca, frame, key, doDecrypt);
        }
    }

    public static void CryptFrame(HcaInfo hca, Span<byte> audio, CriHcaKey key, bool doDecrypt)
    {
        var substitutionTable = doDecrypt ? key.DecryptionTable : key.EncryptionTable;

        var data = audio[..(hca.FrameSize - 2)];
        for (var i = 0; i < data.Length; i++) data[i] = substitutionTable[data[i]];
        var crc = Crc.Compute(data);

        audio[hca.FrameSize - 2] = (byte)(crc >> 8);
        audio[hca.FrameSize - 1] = (byte)crc;
    }

    public static CriHcaKey FindKey(HcaInfo hca, byte[][] audio)
    {
        var frame = new CriHcaFrame(hca);
        var buffer = new byte[hca.FrameSize];
        foreach (var key in Keys)
        {
            if (TestKey(frame, audio, key, buffer))
            {
                return key;
            }
        }
        return null;
    }

    private static bool TestKey(CriHcaFrame frame, byte[][] audio, CriHcaKey key, byte[] buffer)
    {
        var startFrame = FindFirstNonEmptyFrame(audio);
        var endFrame = Math.Min(audio.Length, startFrame + FRAMES_TO_TEST);
        for (var i = startFrame; i < endFrame; i++)
        {
            audio[i].AsSpan().CopyTo(buffer);
            CryptFrame(frame.Hca, buffer, key, true);
            var reader = new BitReader(buffer);
            if (!CriHcaPacking.UnpackFrame(frame, reader))
            {
                return false;
            }
        }
        return true;
    }

    private static int FindFirstNonEmptyFrame(byte[][] frames)
    {
        for (var i = 0; i < frames.Length; i++)
        {
            if (!FrameEmpty(frames[i])) return i;
        }
        return 0;
    }

    private static bool FrameEmpty(ReadOnlySpan<byte> frame)
    {
        var data = frame[2..^2];
        foreach (var b in data)
        {
            if (b != 0) return false;
        }
        return true;
    }
}