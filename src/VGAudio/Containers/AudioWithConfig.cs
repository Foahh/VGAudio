using VGAudio.Formats;

namespace VGAudio.Containers;

public class AudioWithConfig(IAudioFormat audioFormat, Configuration configuration)
{
    public IAudioFormat AudioFormat { get; } = audioFormat;
    public AudioData Audio => new(AudioFormat);
    public Configuration Configuration { get; } = configuration;
}