using System;
using System.IO;
using System.Text;

namespace VGAudio.Utilities;

public class BinaryWriterBe : BinaryWriter
{
    private readonly byte[] buffer = new byte[8];

    public BinaryWriterBe(Stream input) : base(input)
    {
    }

    public BinaryWriterBe(Stream output, Encoding encoding, bool leaveOpen) : base(output, encoding, leaveOpen)
    {
    }

    public override void Write(short value)
    {
        base.Write(Byte.ByteSwap(value));
    }

    public override void Write(ushort value)
    {
        base.Write(Byte.ByteSwap(value));
    }

    public override void Write(int value)
    {
        base.Write(Byte.ByteSwap(value));
    }

    public override void Write(uint value)
    {
        base.Write(Byte.ByteSwap(value));
    }

    public override void Write(long value)
    {
        base.Write(Byte.ByteSwap(value));
    }

    public override void Write(ulong value)
    {
        base.Write(Byte.ByteSwap(value));
    }

    public override void Write(float value)
    {
        var valueBytes = BitConverter.GetBytes(value);

        buffer[0] = valueBytes[3];
        buffer[1] = valueBytes[2];
        buffer[2] = valueBytes[1];
        buffer[3] = valueBytes[0];

        OutStream.Write(buffer, 0, 4);
    }

    public override void Write(double value)
    {
        Write(BitConverter.DoubleToInt64Bits(value));
    }
}