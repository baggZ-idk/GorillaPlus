using System;

namespace GorillaNetworking
{
    public sealed class VoiceJitterBuffer : IDisposable
    {
        const int Slots = 64;
        const int Mask = Slots - 1;
        const int PrebufferFrames = 4;
        const int MaxDepthFrames = 12;
        const int IdleResetMs = 500;

        readonly object gate = new object();

        readonly byte[][] slotData = new byte[Slots][];
        readonly int[] slotLen = new int[Slots];
        readonly ushort[] slotSeq = new ushort[Slots];
        readonly bool[] slotUsed = new bool[Slots];

        readonly OpusDecoderWrapper decoder = new OpusDecoderWrapper();
        readonly float[] cur = new float[VoiceConstants.FrameSamples];
        int curPos = VoiceConstants.FrameSamples;

        bool hasBase;
        bool playing;
        bool hasEnd;
        bool disposed;
        ushort nextSeq;
        ushort highSeq;
        ushort endSeq;
        int lastPushTick;

        public VoiceJitterBuffer()
        {
            for (int i = 0; i < Slots; i++) slotData[i] = new byte[VoiceConstants.MaxEncodedFrameBytes];
        }
        public int Depth
        {
            get
            {
                lock (gate)
                {
                    return hasBase ? Math.Max(0, (short)(highSeq - nextSeq) + 1) : 0;
                }
            }
        }

        public void Push(ushort firstSeq, byte flags, byte[] data)
        {
            if (data == null) return;

            lock (gate)
            {
                if (disposed) return;

                if ((flags & VoiceConstants.FlagStart) != 0) ResetLocked();
                lastPushTick = Environment.TickCount;

                ushort seq = firstSeq;
                int pos = 0;
                while (pos < data.Length)
                {
                    int len = data[pos++];
                    if (len == 0) continue;
                    if (len > VoiceConstants.MaxEncodedFrameBytes || pos + len > data.Length) break;

                    StoreLocked(seq, data, pos, len);
                    seq++;
                    pos += len;
                }

                if ((flags & VoiceConstants.FlagEnd) != 0)
                {
                    hasEnd = true;
                    endSeq = seq;
                }
            }
        }

        void StoreLocked(ushort seq, byte[] src, int offset, int len)
        {
            if (!hasBase)
            {
                hasBase = true;
                playing = false;
                hasEnd = false;
                nextSeq = seq;
                highSeq = seq;
            }

            short d = (short)(seq - nextSeq);
            if (d < 0) return;                       // too late, already played or skipped

            if (d >= Slots - 1)                      // way ahead of us: resync to this packet
            {
                ClearSlotsLocked();
                nextSeq = seq;
                highSeq = seq;
                playing = false;
            }

            int i = seq & Mask;
            Buffer.BlockCopy(src, offset, slotData[i], 0, len);
            slotLen[i] = len;
            slotSeq[i] = seq;
            slotUsed[i] = true;

            if ((short)(seq - highSeq) > 0) highSeq = seq;
        }

        // ---------------- audio thread ----------------

        /// <summary>Fills dst[0..count) with mono 48 kHz samples; pads with silence if there is no audio.</summary>
        public void Read(float[] dst, int count)
        {
            int w = 0;

            lock (gate)
            {
                if (!disposed)
                {
                    while (w < count)
                    {
                        if (curPos >= VoiceConstants.FrameSamples && !FillNextLocked()) break;

                        int n = Math.Min(count - w, VoiceConstants.FrameSamples - curPos);
                        Array.Copy(cur, curPos, dst, w, n);
                        curPos += n;
                        w += n;
                    }
                }
            }

            while (w < count) dst[w++] = 0f;
        }

        bool FillNextLocked()
        {
            if (!hasBase) return false;

            int depth = (short)(highSeq - nextSeq) + 1;

            if (!playing)
            {
                if (depth >= PrebufferFrames || (hasEnd && depth > 0))
                {
                    playing = true;
                }
                else
                {
                    CheckIdleLocked();
                    return false;
                }
            }

            // Too much queued: jump forward so latency does not creep up.
            if (depth > MaxDepthFrames)
            {
                nextSeq = (ushort)(highSeq - PrebufferFrames + 1);
            }

            int i = nextSeq & Mask;
            if (slotUsed[i] && slotSeq[i] == nextSeq)
            {
                int n = decoder.Decode(slotData[i], slotLen[i], cur);
                slotUsed[i] = false;
                if (n < VoiceConstants.FrameSamples) Array.Clear(cur, Math.Max(0, n), VoiceConstants.FrameSamples - Math.Max(0, n));
            }
            else if ((short)(highSeq - nextSeq) > 0)
            {
                // This frame is missing but a later one has arrived: conceal it.
                decoder.Conceal(cur);
            }
            else
            {
                // Nothing buffered at all.
                if (hasEnd && (short)(endSeq - nextSeq) <= 0)
                {
                    ResetLocked();
                    return false;
                }

                playing = false;     // underrun: rebuffer
                CheckIdleLocked();
                return false;
            }

            nextSeq++;
            curPos = 0;

            if (hasEnd && (short)(endSeq - nextSeq) <= 0)
            {
                // That was the last frame of the spurt. Let it finish playing, then go idle.
                hasBase = false;
                playing = false;
                hasEnd = false;
                ClearSlotsLocked();
            }

            return true;
        }

        void CheckIdleLocked()
        {
            if (Environment.TickCount - lastPushTick > IdleResetMs) ResetLocked();
        }

        void ResetLocked()
        {
            ClearSlotsLocked();
            hasBase = false;
            playing = false;
            hasEnd = false;
            curPos = VoiceConstants.FrameSamples;
        }

        void ClearSlotsLocked()
        {
            for (int i = 0; i < Slots; i++) slotUsed[i] = false;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                decoder.Dispose();
            }
        }
    }
}
