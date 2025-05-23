using System;
using System.IO;
using System.Linq;

namespace VGAudio.Utilities;

public static class InterleaveExtensions
{
    public static T[] Interleave<T>(this T[][] inputs, int interleaveSize, int outputSize = -1)
    {
        var inputSize = inputs[0].Length;
        if (outputSize == -1)
            outputSize = inputSize;

        if (inputs.Any(x => x.Length != inputSize))
            throw new ArgumentOutOfRangeException(nameof(inputs), "Inputs must be of equal length");

        var inputCount = inputs.Length;
        var inBlockCount = inputSize.DivideByRoundUp(interleaveSize);
        var outBlockCount = outputSize.DivideByRoundUp(interleaveSize);
        var lastInputInterleaveSize = inputSize - (inBlockCount - 1) * interleaveSize;
        var lastOutputInterleaveSize = outputSize - (outBlockCount - 1) * interleaveSize;
        var blocksToCopy = Math.Min(inBlockCount, outBlockCount);

        var output = new T[outputSize * inputCount];

        for (var b = 0; b < blocksToCopy; b++)
        {
            var currentInputInterleaveSize = b == inBlockCount - 1 ? lastInputInterleaveSize : interleaveSize;
            var currentOutputInterleaveSize = b == outBlockCount - 1 ? lastOutputInterleaveSize : interleaveSize;
            var bytesToCopy = Math.Min(currentInputInterleaveSize, currentOutputInterleaveSize);

            for (var i = 0; i < inputCount; i++)
            {
                Array.Copy(inputs[i], interleaveSize * b, output,
                    interleaveSize * b * inputCount + currentOutputInterleaveSize * i, bytesToCopy);
            }
        }

        return output;
    }

    public static void Interleave(this byte[][] inputs, Stream output, int interleaveSize, int outputSize = -1)
    {
        var inputSize = inputs[0].Length;
        if (outputSize == -1)
            outputSize = inputSize;

        if (inputs.Any(x => x.Length != inputSize))
            throw new ArgumentOutOfRangeException(nameof(inputs), "Inputs must be of equal length");

        var inputCount = inputs.Length;
        var inBlockCount = inputSize.DivideByRoundUp(interleaveSize);
        var outBlockCount = outputSize.DivideByRoundUp(interleaveSize);
        var lastInputInterleaveSize = inputSize - (inBlockCount - 1) * interleaveSize;
        var lastOutputInterleaveSize = outputSize - (outBlockCount - 1) * interleaveSize;
        var blocksToCopy = Math.Min(inBlockCount, outBlockCount);

        for (var b = 0; b < blocksToCopy; b++)
        {
            var currentInputInterleaveSize = b == inBlockCount - 1 ? lastInputInterleaveSize : interleaveSize;
            var currentOutputInterleaveSize = b == outBlockCount - 1 ? lastOutputInterleaveSize : interleaveSize;
            var bytesToCopy = Math.Min(currentInputInterleaveSize, currentOutputInterleaveSize);

            for (var i = 0; i < inputCount; i++)
            {
                output.Write(inputs[i], interleaveSize * b, bytesToCopy);
                if (bytesToCopy < currentOutputInterleaveSize)
                {
                    output.Position += currentOutputInterleaveSize - bytesToCopy;
                }
            }
        }

        //Simply setting the position past the end of the stream doesn't expand the stream,
        //so we do that manually if necessary
        output.SetLength(Math.Max(outputSize * inputCount, output.Length));
    }

    public static T[][] DeInterleave<T>(this T[] input, int interleaveSize, int outputCount, int outputSize = -1)
    {
        if (input.Length % outputCount != 0)
            throw new ArgumentOutOfRangeException(nameof(outputCount), outputCount,
                $"The input array length ({input.Length}) must be divisible by the number of outputs.");

        var inputSize = input.Length / outputCount;
        if (outputSize == -1)
            outputSize = inputSize;

        var inBlockCount = inputSize.DivideByRoundUp(interleaveSize);
        var outBlockCount = outputSize.DivideByRoundUp(interleaveSize);
        var lastInputInterleaveSize = inputSize - (inBlockCount - 1) * interleaveSize;
        var lastOutputInterleaveSize = outputSize - (outBlockCount - 1) * interleaveSize;
        var blocksToCopy = Math.Min(inBlockCount, outBlockCount);

        var outputs = new T[outputCount][];
        for (var i = 0; i < outputCount; i++)
        {
            outputs[i] = new T[outputSize];
        }

        for (var b = 0; b < blocksToCopy; b++)
        {
            var currentInputInterleaveSize = b == inBlockCount - 1 ? lastInputInterleaveSize : interleaveSize;
            var currentOutputInterleaveSize = b == outBlockCount - 1 ? lastOutputInterleaveSize : interleaveSize;
            var bytesToCopy = Math.Min(currentInputInterleaveSize, currentOutputInterleaveSize);

            for (var o = 0; o < outputCount; o++)
            {
                Array.Copy(input, interleaveSize * b * outputCount + currentInputInterleaveSize * o, outputs[o],
                    interleaveSize * b, bytesToCopy);
            }
        }

        return outputs;
    }

    public static byte[][] DeInterleave(this Stream input, int length, int interleaveSize, int outputCount, int outputSize = -1)
    {
        if (input.CanSeek)
        {
            var remainingLength = input.Length - input.Position;
            if (remainingLength < length)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length,
                    "Specified length is greater than the number of bytes remaining in the Stream");
            }
        }

        if (length % outputCount != 0)
            throw new ArgumentOutOfRangeException(nameof(outputCount), outputCount,
                $"The input length ({length}) must be divisible by the number of outputs.");

        var inputSize = length / outputCount;
        if (outputSize == -1)
            outputSize = inputSize;

        var inBlockCount = inputSize.DivideByRoundUp(interleaveSize);
        var outBlockCount = outputSize.DivideByRoundUp(interleaveSize);
        var lastInputInterleaveSize = inputSize - (inBlockCount - 1) * interleaveSize;
        var lastOutputInterleaveSize = outputSize - (outBlockCount - 1) * interleaveSize;
        var blocksToCopy = Math.Min(inBlockCount, outBlockCount);

        var outputs = new byte[outputCount][];
        for (var i = 0; i < outputCount; i++)
        {
            outputs[i] = new byte[outputSize];
        }

        for (var b = 0; b < blocksToCopy; b++)
        {
            var currentInputInterleaveSize = b == inBlockCount - 1 ? lastInputInterleaveSize : interleaveSize;
            var currentOutputInterleaveSize = b == outBlockCount - 1 ? lastOutputInterleaveSize : interleaveSize;
            var bytesToCopy = Math.Min(currentInputInterleaveSize, currentOutputInterleaveSize);

            for (var o = 0; o < outputCount; o++)
            {
                input.ReadExactly(outputs[o], interleaveSize * b, bytesToCopy);
                if (bytesToCopy < currentInputInterleaveSize)
                {
                    input.Position += currentInputInterleaveSize - bytesToCopy;
                }
            }
        }

        return outputs;
    }

    public static byte[] ShortToInterleavedByte(this short[][] input)
    {
        var inputCount = input.Length;
        var length = input[0].Length;
        var output = new byte[inputCount * length * 2];

        for (var i = 0; i < length; i++)
        {
            for (var j = 0; j < inputCount; j++)
            {
                var offset = (i * inputCount + j) * 2;
                output[offset] = (byte)input[j][i];
                output[offset + 1] = (byte)(input[j][i] >> 8);
            }
        }

        return output;
    }

    public static short[][] InterleavedByteToShort(this byte[] input, int outputCount)
    {
        var itemCount = input.Length / 2 / outputCount;
        var output = new short[outputCount][];
        for (var i = 0; i < outputCount; i++)
        {
            output[i] = new short[itemCount];
        }

        for (var i = 0; i < itemCount; i++)
        {
            for (var o = 0; o < outputCount; o++)
            {
                var offset = (i * outputCount + o) * 2;
                output[o][i] = (short)(input[offset] | input[offset + 1] << 8);
            }
        }

        return output;
    }
}