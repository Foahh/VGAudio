using System;

namespace VGAudio.Codecs;

public class CodecParameters
{
    public CodecParameters()
    {
    }

    protected CodecParameters(CodecParameters source)
    {
        if (source == null) return;
        Progress = source.Progress;
        SampleCount = source.SampleCount;
    }

    public IProgress<double> Progress { get; set; }
    public int SampleCount { get; set; } = -1;
}