using System;
using UnityOpus;

namespace GorillaNetworking
{
    public sealed class OpusDecoderWrapper : IDisposable
    {
        readonly Decoder decoder;

        public OpusDecoderWrapper()
        {
            decoder = new Decoder(SamplingFrequency.Frequency_48000, NumChannels.Mono);
        }

        public int Decode(byte[] data, int length, float[] pcm)
        {
            return decoder.Decode(data, length, pcm);
        }

        public int Conceal(float[] pcm)
        {
            return decoder.Decode(null, 0, pcm);
        }

        public void Dispose()
        {
            decoder.Dispose();
        }
    }
}
