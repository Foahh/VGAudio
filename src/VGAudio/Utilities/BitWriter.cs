using System;
using System.Diagnostics;

namespace VGAudio.Utilities;

public class BitWriter
{
    public BitWriter(byte[] buffer)
    {
        Buffer = buffer;
        LengthBits = Buffer.Length * 8;
    }

    public byte[] Buffer { get; }
    public int LengthBits { get; }
    public int Position { get; set; }
    public int Remaining => LengthBits - Position;

    public void AlignPosition(int multiple)
    {
        var newPosition = Helpers.GetNextMultiple(Position, multiple);
        var bits = newPosition - Position;
        Write(0, bits);
    }

    public void Write(int value, int bitCount)
    {
        Debug.Assert(bitCount is >= 0 and <= 32);

        if (bitCount > Remaining)
        {
            throw new InvalidOperationException("Not enough bits left in output buffer");
        }

        var byteIndex = Position / 8;
        var bitIndex = Position % 8;

        if (bitCount <= 9 && Remaining >= 16)
        {
            var outValue = (value << 16 - bitCount & 0xFFFF) >> bitIndex;

            Buffer[byteIndex] |= (byte)(outValue >> 8);
            Buffer[byteIndex + 1] = (byte)outValue;
        }

        else if (bitCount <= 17 && Remaining >= 24)
        {
            var outValue = (value << 24 - bitCount & 0xFFFFFF) >> bitIndex;

            Buffer[byteIndex] |= (byte)(outValue >> 16);
            Buffer[byteIndex + 1] = (byte)(outValue >> 8);
            Buffer[byteIndex + 2] = (byte)outValue;
        }

        else if (bitCount <= 25 && Remaining >= 32)
        {
            var outValue = (int)((value << 32 - bitCount & 0xFFFFFFFF) >> bitIndex);

            Buffer[byteIndex] |= (byte)(outValue >> 24);
            Buffer[byteIndex + 1] = (byte)(outValue >> 16);
            Buffer[byteIndex + 2] = (byte)(outValue >> 8);
            Buffer[byteIndex + 3] = (byte)outValue;
        }
        else
        {
            WriteFallback(value, bitCount);
        }

        Position += bitCount;
    }

    private void WriteFallback(int value, int bitCount)
    {
        var byteIndex = Position / 8;
        var bitIndex = Position % 8;

        while (bitCount > 0)
        {
            if (bitIndex >= 8)
            {
                bitIndex = 0;
                byteIndex++;
            }

            var toShift = 8 - bitIndex - bitCount;
            var shifted = toShift < 0 ? value >> -toShift : value << toShift;
            var bitsToWrite = Math.Min(bitCount, 8 - bitIndex);

            var mask = (1 << bitsToWrite) - 1 << 8 - bitIndex - bitsToWrite;
            var outByte = Buffer[byteIndex] & ~mask;
            outByte |= shifted & mask;
            Buffer[byteIndex] = (byte)outByte;

            bitIndex += bitsToWrite;
            bitCount -= bitsToWrite;
        }
    }
}