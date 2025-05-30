using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace VGAudio.Utilities;

public class BinaryReaderBe : BinaryReader
{
    public BinaryReaderBe(Stream input) : base(input)
    {
    }

    public BinaryReaderBe(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
    {
    }

    public override short ReadInt16()
    {
        return Byte.ByteSwap(base.ReadInt16());
    }

    public override ushort ReadUInt16()
    {
        return Byte.ByteSwap(base.ReadUInt16());
    }

    public override int ReadInt32()
    {
        return Byte.ByteSwap(base.ReadInt32());
    }

    public override uint ReadUInt32()
    {
        return Byte.ByteSwap(base.ReadUInt32());
    }

    public override long ReadInt64()
    {
        return Byte.ByteSwap(base.ReadInt64());
    }

    public override ulong ReadUInt64()
    {
        return Byte.ByteSwap(base.ReadUInt64());
    }

    public override float ReadSingle()
    {
        Span<byte> buffer = stackalloc byte[4];
        BaseStream.ReadExactly(buffer);
        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    public override double ReadDouble()
    {
        return BitConverter.Int64BitsToDouble(ReadInt64());
    }
}