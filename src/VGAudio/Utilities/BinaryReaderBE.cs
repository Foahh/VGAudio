using System;
using System.IO;
using System.Text;

namespace VGAudio.Utilities;

public class BinaryReaderBe : BinaryReader
{
    private readonly byte[] bufferIn = new byte[8];
    private readonly byte[] bufferOut = new byte[8];

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
        BaseStream.ReadExactly(bufferIn, 0, 4);

        bufferOut[0] = bufferIn[3];
        bufferOut[1] = bufferIn[2];
        bufferOut[2] = bufferIn[1];
        bufferOut[3] = bufferIn[0];

        return BitConverter.ToSingle(bufferOut, 0);
    }

    public override double ReadDouble()
    {
        return BitConverter.Int64BitsToDouble(ReadInt64());
    }
}