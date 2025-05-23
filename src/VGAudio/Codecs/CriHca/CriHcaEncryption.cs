﻿using System;
using VGAudio.Utilities;

namespace VGAudio.Codecs.CriHca;

public static partial class CriHcaEncryption
{
    private const int FRAMES_TO_TEST = 10;

    private static Crc16 Crc { get; } = new(0x8005);

    public static void Crypt(HcaInfo hca, byte[][] audio, CriHcaKey key, bool doDecrypt)
    {
        for (var frame = 0; frame < hca.FrameCount; frame++)
        {
            CryptFrame(hca, audio[frame], key, doDecrypt);
        }
    }

    public static void CryptFrame(HcaInfo hca, byte[] audio, CriHcaKey key, bool doDecrypt)
    {
        var substitutionTable = doDecrypt ? key.DecryptionTable : key.EncryptionTable;

        for (var b = 0; b < hca.FrameSize - 2; b++)
        {
            audio[b] = substitutionTable[audio[b]];
        }

        var crc = Crc.Compute(audio, hca.FrameSize - 2);
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
            Array.Copy(audio[i], buffer, audio[i].Length);
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
            if (!FrameEmpty(frames[i]))
            {
                return i;
            }
        }
        return 0;
    }

    private static bool FrameEmpty(byte[] frame)
    {
        for (var i = 2; i < frame.Length - 2; i++)
        {
            if (frame[i] != 0)
            {
                return false;
            }
        }
        return true;
    }
}