using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace VGAudio.Utilities;

public class BinaryWriterBe : BinaryWriter
{
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
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        OutStream.Write(buffer);
    }

    public override void Write(double value)
    {
        Write(BitConverter.DoubleToInt64Bits(value));
    }
}