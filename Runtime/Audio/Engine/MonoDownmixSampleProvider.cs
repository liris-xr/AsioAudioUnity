using NAudio.Wave;

namespace AsioAudioUnity
{
    /// <summary>
    /// Mixes all channels of a sample provider down to a single (mono) channel, by averaging them.
    /// </summary>
    internal sealed class MonoDownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceChannels;
        private float[] _sourceBuffer = new float[0];

        public WaveFormat WaveFormat { get; }

        public MonoDownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            _sourceChannels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesNeeded = count * _sourceChannels;
            if (_sourceBuffer.Length < samplesNeeded) _sourceBuffer = new float[samplesNeeded];

            int framesRead = _source.Read(_sourceBuffer, 0, samplesNeeded) / _sourceChannels;
            float channelWeight = 1f / _sourceChannels;

            for (int frame = 0; frame < framesRead; frame++)
            {
                float sum = 0f;
                int frameStart = frame * _sourceChannels;
                for (int channel = 0; channel < _sourceChannels; channel++) sum += _sourceBuffer[frameStart + channel];
                buffer[offset + frame] = sum * channelWeight;
            }

            return framesRead;
        }
    }
}
