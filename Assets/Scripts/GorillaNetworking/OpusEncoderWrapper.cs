using System;
using UnityOpus;

namespace GorillaNetworking
{
    public sealed class OpusEncoderWrapper : IDisposable
    {
        readonly Encoder encoder;
        readonly byte[] scratch = new byte[VoiceConstants.MaxEncodedFrameBytes];

        public OpusEncoderWrapper()
        {
            encoder = new Encoder(
                SamplingFrequency.Frequency_48000,
                NumChannels.Mono,
                OpusApplication.VoIP);

            encoder.Bitrate = VoiceConstants.Bitrate;
            encoder.Complexity = 5;
            encoder.Signal = OpusSignal.Voice;
        }

        public int Encode(float[] pcm, byte[] dst, int dstOffset)
        {
            int n = encoder.Encode(pcm, scratch);
            if (n <= 0 || dstOffset + n > dst.Length) return 0;
            Buffer.BlockCopy(scratch, 0, dst, dstOffset, n);
            return n;
        }

        public void Dispose()
        {
            encoder.Dispose();
        }
    }
}
