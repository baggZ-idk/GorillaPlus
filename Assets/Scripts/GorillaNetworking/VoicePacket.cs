namespace GorillaNetworking
{
    public struct VoicePacket
    {
        public ushort Seq;
        public byte Flags;
        public byte[] Data;
    }

    public static class VoiceStats
    {
        public static int PacketsSent;
        public static int BytesSent;
        public static int PacketsReceived;
        public static int BytesReceived;
        public static int PacketsDropped;
    }
}
