using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace VGAudio.Utilities;

public static class ArrayUnpacker
{
    private static readonly Type[] TypeLookup =
    [
        typeof(byte),
        typeof(sbyte),
        typeof(char),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double)
    ];

    public static Array[] UnpackArrays(byte[] packedArrays)
    {
        packedArrays = TryDecompress(packedArrays);

        using var stream = new MemoryStream(packedArrays);
        using var reader = new BinaryReader(stream);
        int compressed = reader.ReadByte();
        int version = reader.ReadByte();
        if (compressed != 0 || version != 0) throw new InvalidDataException();

        int count = reader.ReadUInt16();
        var arrays = new Array[count];

        for (var i = 0; i < count; i++)
        {
            var id = reader.ReadByte();
            var type = reader.ReadByte();
            var outType = TypeLookup[Helpers.GetHighNibble(type)];
            var rank = Helpers.GetLowNibble(type);
            arrays[id] = UnpackArray(reader, outType, rank);
        }

        return arrays;
    }

    private static Array UnpackArray(BinaryReader reader, Type outType, int rank)
    {
        var modeType = reader.ReadByte();
        if (modeType == byte.MaxValue) return null;

        var mode = Helpers.GetHighNibble(modeType);
        var storedType = TypeLookup[Helpers.GetLowNibble(modeType)];
        var elementType = Arrays.MakeJaggedArrayType(outType, rank - 1);

        switch (mode)
        {
            case 0:
                {
                    int length = reader.ReadUInt16();

                    if (rank == 1)
                    {
                        return ReadArray(reader, storedType, elementType, length);
                    }

                    var array = Array.CreateInstance(elementType, length);

                    for (var i = 0; i < length; i++)
                    {
                        array.SetValue(UnpackArray(reader, outType, rank - 1), i);
                    }

                    return array;
                }
            case 1:
                {
                    var dimensions = new int[rank];

                    for (var d = 0; d < dimensions.Length; d++)
                    {
                        dimensions[d] = reader.ReadUInt16();
                    }

                    return UnpackInternal(elementType, storedType, reader, 0, dimensions);
                }
            case 2:
                {
                    int length = reader.ReadUInt16();
                    var lengths = new int[length];

                    for (var i = 0; i < length; i++)
                    {
                        lengths[i] = reader.ReadUInt16();
                    }

                    var array = Array.CreateInstance(elementType, length);

                    for (var i = 0; i < length; i++)
                    {
                        array.SetValue(ReadArray(reader, storedType, outType, lengths[i]), i);
                    }

                    return array;
                }

            default:
                throw new InvalidDataException();
        }
    }

    private static Array ReadArray(BinaryReader reader, Type storedType, Type outType, int length)
    {
        if (length == ushort.MaxValue) return null;

        var lengthBytes = length * Marshal.SizeOf(storedType);
        var array = Array.CreateInstance(storedType, length);
        var bytes = reader.ReadBytes(lengthBytes);
        Buffer.BlockCopy(bytes, 0, array, 0, lengthBytes);

        return storedType == outType ? array : CastArray(array, outType);
    }

    private static Array CastArray(Array inArray, Type outType)
    {
        var outArray = Array.CreateInstance(outType, inArray.Length);

        for (var i = 0; i < inArray.Length; i++)
        {
            var inValue = inArray.GetValue(i);
            var outValue = Convert.ChangeType(inValue, outType);
            outArray.SetValue(outValue, i);
        }
        return outArray;
    }

    private static byte[] TryDecompress(byte[] data)
    {
        var compressed = data[0] == 1;
        if (compressed)
        {
            var decompressedLength = BitConverter.ToInt32(data, 1);
            data = Inflate(data, 5, decompressedLength);
        }
        return data;
    }

    private static byte[] Inflate(byte[] compressed, int startIndex, int length)
    {
        var inflatedBytes = new byte[length];
        using var stream = new MemoryStream(compressed);
        stream.Position = startIndex;
        using var deflate = new DeflateStream(stream, CompressionMode.Decompress);
        deflate.ReadExactly(inflatedBytes, 0, length);

        return inflatedBytes;
    }

    private static Array UnpackInternal(Type outType, Type storedType, BinaryReader reader, int depth, int[] dimensions)
    {
        if (depth >= dimensions.Length) return null;
        if (depth == dimensions.Length - 1)
        {
            return ReadArray(reader, storedType, outType, dimensions[depth]);
        }

        var array = Array.CreateInstance(outType, dimensions[depth]);

        for (var i = 0; i < dimensions[depth]; i++)
        {
            array.SetValue(UnpackInternal(outType.GetElementType(), storedType, reader, depth + 1, dimensions), i);
        }

        return array;
    }
}