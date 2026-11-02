namespace GorillaNetworking
{
    public static class VoiceConstants
    {
        public const int SampleRate = 48000;
        public const int FrameMs = 20;
        public const int FrameSamples = SampleRate * FrameMs / 1000;

        public const int FramesPerPacket = 3;
        public const int MaxEncodedFrameBytes = 250;
        public const int MaxPacketBytes = FramesPerPacket * (1 + MaxEncodedFrameBytes);

        public const int Bitrate = 24000;

        public const byte FlagStart = 1;
        public const byte FlagEnd = 2;
    }
}
