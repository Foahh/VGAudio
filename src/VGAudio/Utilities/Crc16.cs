using System;

namespace VGAudio.Utilities;

public class Crc16(ushort polynomial)
{
    private readonly ushort[] table = GenerateTable(polynomial);

    public ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data) crc = (ushort)(crc << 8 ^ table[crc >> 8 ^ b]);
        return crc;
    }

    private static ushort[] GenerateTable(ushort polynomial)
    {
        var table = new ushort[256];
        for (var i = 0; i < table.Length; i++)
        {
            var curByte = (ushort)(i << 8);
            for (byte j = 0; j < 8; j++)
            {
                var xorFlag = (curByte & 0x8000) != 0;
                curByte <<= 1;
                if (xorFlag) curByte ^= polynomial;
            }
            table[i] = curByte;
        }
        return table;
    }
}