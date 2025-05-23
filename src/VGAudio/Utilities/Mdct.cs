using System;
using System.Collections.Generic;

namespace VGAudio.Utilities;

public class Mdct
{
    private static readonly object TableLock = new();
    private static int _tableBits = -1;
    private static readonly List<double[]> SinTables =
    [
    ];
    private static readonly List<double[]> CosTables =
    [
    ];
    private static readonly List<int[]> ShuffleTables =
    [
    ];
    private readonly double[] imdctPrevious;
    private readonly double[] imdctWindow;

    private readonly double[] mdctPrevious;
    private readonly double[] scratchDct;

    private readonly double[] scratchMdct;

    public Mdct(int mdctBits, double[] window, double scale = 1)
    {
        SetTables(mdctBits);

        MdctBits = mdctBits;
        MdctSize = 1 << mdctBits;
        Scale = scale;

        if (window.Length < MdctSize)
        {
            throw new ArgumentException("Window must be as long as the MDCT size.", nameof(window));
        }

        mdctPrevious = new double[MdctSize];
        imdctPrevious = new double[MdctSize];
        scratchMdct = new double[MdctSize];
        scratchDct = new double[MdctSize];
        imdctWindow = window;
    }

    public int MdctBits { get; }
    public int MdctSize { get; }
    public double Scale { get; }

    private static void SetTables(int maxBits)
    {
        lock (TableLock)
        {
            if (maxBits > _tableBits)
            {
                for (var i = _tableBits + 1; i <= maxBits; i++)
                {
                    GenerateTrigTables(i, out var sin, out var cos);
                    SinTables.Add(sin);
                    CosTables.Add(cos);
                    ShuffleTables.Add(GenerateShuffleTable(i));
                }
                _tableBits = maxBits;
            }
        }
    }

    public void RunMdct(double[] input, double[] output)
    {
        if (input.Length < MdctSize)
        {
            throw new ArgumentException("Input must be as long as the MDCT size.", nameof(input));
        }

        if (output.Length < MdctSize)
        {
            throw new ArgumentException("Output must be as long as the MDCT size.", nameof(output));
        }

        var size = MdctSize;
        var half = size / 2;
        var dctIn = scratchMdct;

        for (var i = 0; i < half; i++)
        {
            var a = imdctWindow[half - i - 1] * -input[half + i];
            var b = imdctWindow[half + i] * input[half - i - 1];
            var c = imdctWindow[i] * mdctPrevious[i];
            var d = imdctWindow[size - i - 1] * mdctPrevious[size - i - 1];

            dctIn[i] = a - b;
            dctIn[half + i] = c - d;
        }

        Dct4(dctIn, output);
        Array.Copy(input, mdctPrevious, input.Length);
    }

    public void RunImdct(double[] input, double[] output)
    {
        if (input.Length < MdctSize)
        {
            throw new ArgumentException("Input must be as long as the MDCT size.", nameof(input));
        }

        if (output.Length < MdctSize)
        {
            throw new ArgumentException("Output must be as long as the MDCT size.", nameof(output));
        }

        var size = MdctSize;
        var half = size / 2;
        var dctOut = scratchMdct;

        Dct4(input, dctOut);

        for (var i = 0; i < half; i++)
        {
            output[i] = imdctWindow[i] * dctOut[i + half] + imdctPrevious[i];
            output[i + half] = imdctWindow[i + half] * -dctOut[size - 1 - i] - imdctPrevious[i + half];
            imdctPrevious[i] = imdctWindow[size - 1 - i] * -dctOut[half - i - 1];
            imdctPrevious[i + half] = imdctWindow[half - i - 1] * dctOut[i];
        }
    }

    /// <summary>
    ///     Does a Type-4 DCT.
    /// </summary>
    /// <param name="input">The input array containing the time or frequency-domain samples</param>
    /// <param name="output">The output array that will contain the transformed time or frequency-domain samples</param>
    private void Dct4(double[] input, double[] output)
    {
        var shuffleTable = ShuffleTables[MdctBits];
        var sinTable = SinTables[MdctBits];
        var cosTable = CosTables[MdctBits];
        var dctTemp = scratchDct;

        var size = MdctSize;
        var lastIndex = size - 1;
        var halfSize = size / 2;

        for (var i = 0; i < halfSize; i++)
        {
            var i2 = i * 2;
            var a = input[i2];
            var b = input[lastIndex - i2];
            var sin = sinTable[i];
            var cos = cosTable[i];
            dctTemp[i2] = a * cos + b * sin;
            dctTemp[i2 + 1] = a * sin - b * cos;
        }
        var stageCount = MdctBits - 1;

        for (var stage = 0; stage < stageCount; stage++)
        {
            var blockCount = 1 << stage;
            var blockSizeBits = stageCount - stage;
            var blockHalfSizeBits = blockSizeBits - 1;
            var blockSize = 1 << blockSizeBits;
            var blockHalfSize = 1 << blockHalfSizeBits;
            sinTable = SinTables[blockHalfSizeBits];
            cosTable = CosTables[blockHalfSizeBits];

            for (var block = 0; block < blockCount; block++)
            {
                for (var i = 0; i < blockHalfSize; i++)
                {
                    var frontPos = (block * blockSize + i) * 2;
                    var backPos = frontPos + blockSize;
                    var a = dctTemp[frontPos] - dctTemp[backPos];
                    var b = dctTemp[frontPos + 1] - dctTemp[backPos + 1];
                    var sin = sinTable[i];
                    var cos = cosTable[i];
                    dctTemp[frontPos] += dctTemp[backPos];
                    dctTemp[frontPos + 1] += dctTemp[backPos + 1];
                    dctTemp[backPos] = a * cos + b * sin;
                    dctTemp[backPos + 1] = a * sin - b * cos;
                }
            }
        }

        for (var i = 0; i < MdctSize; i++)
        {
            output[i] = dctTemp[shuffleTable[i]] * Scale;
        }
    }

    internal static void GenerateTrigTables(int sizeBits, out double[] sin, out double[] cos)
    {
        var size = 1 << sizeBits;
        sin = new double[size];
        cos = new double[size];

        for (var i = 0; i < size; i++)
        {
            var value = Math.PI * (4 * i + 1) / (4 * size);
            sin[i] = Math.Sin(value);
            cos[i] = Math.Cos(value);
        }
    }

    internal static int[] GenerateShuffleTable(int sizeBits)
    {
        var size = 1 << sizeBits;
        var table = new int[size];

        for (var i = 0; i < size; i++)
        {
            table[i] = Bit.BitReverse32(i ^ i / 2, sizeBits);
        }

        return table;
    }

    // ReSharper disable once UnusedMember.Local
    /// <summary>
    ///     Does a Type-4 DCT. Intended for reference.
    /// </summary>
    /// <param name="input">The input array containing the time or frequency-domain samples</param>
    /// <param name="output">The output array that will contain the transformed time or frequency-domain samples</param>
    private void Dct4Slow(double[] input, double[] output)
    {
        for (var k = 0; k < MdctSize; k++)
        {
            double sample = 0;
            for (var n = 0; n < MdctSize; n++)
            {
                var angle = Math.PI / MdctSize * (k + 0.5) * (n + 0.5);
                sample += Math.Cos(angle) * input[n];
            }
            output[k] = sample * Scale;
        }
    }
}