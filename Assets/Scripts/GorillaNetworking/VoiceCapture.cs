using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace GorillaNetworking
{
    public sealed class VoiceCapture
    {
        readonly ConcurrentQueue<VoicePacket> outbox;
        readonly ConcurrentQueue<float[]> frames = new ConcurrentQueue<float[]>();
        readonly ConcurrentQueue<float[]> framePool = new ConcurrentQueue<float[]>();
        readonly AutoResetEvent signal = new AutoResetEvent(false);

        public volatile bool Muted;
        public volatile bool UsePushToTalk;
        public volatile bool PushToTalkHeld;
        public float VadThreshold = 0.01f;
        public int HangoverFrames = 10;

        AudioClip clip;
        int clipSamples;
        int readPos;
        Thread worker;
        volatile bool running;

        readonly byte[] bundle = new byte[VoiceConstants.MaxPacketBytes];
        int bundleLen;
        int bundleFrames;
        ushort bundleFirstSeq;
        ushort nextSeq;
        bool startPending;

        public bool IsRunning => running;

        public VoiceCapture(ConcurrentQueue<VoicePacket> outbox)
        {
            this.outbox = outbox;
        }

        public void Start()
        {
            if (running) return;

            clip = Microphone.Start(null, true, 1, VoiceConstants.SampleRate);
            if (clip == null)
            {
                Debug.LogError("[GorillaNetworking] Microphone.Start failed (permission denied or no mic).");
                return;
            }

            clipSamples = clip.samples;
            if (clip.frequency != VoiceConstants.SampleRate || clipSamples % VoiceConstants.FrameSamples != 0)
            {
                Debug.LogError($"[GorillaNetworking] Mic gave {clip.frequency} Hz / {clipSamples} samples; need 48000 Hz. Resampling is not implemented.");
                Microphone.End(null);
                clip = null;
                return;
            }

            readPos = 0;
            running = true;
            worker = new Thread(WorkerLoop) { IsBackground = true, Name = "VoiceEncode" };
            worker.Start();
        }

        public void Stop()
        {
            if (!running && clip == null) return;
            running = false;
            signal.Set();
            worker?.Join(200);
            worker = null;

            if (clip != null)
            {
                Microphone.End(null);
                clip = null;
            }
        }

        public void Tick()
        {
            if (!running || clip == null) return;

            int pos = Microphone.GetPosition(null);
            if (pos < 0) return;

            int avail = pos - readPos;
            if (avail < 0) avail += clipSamples;

            if (avail > clipSamples * 3 / 4)
            {
                readPos = pos - (pos % VoiceConstants.FrameSamples);
                return;
            }

            bool any = false;
            while (avail >= VoiceConstants.FrameSamples)
            {
                if (frames.Count > 50) break;

                if (!framePool.TryDequeue(out var f)) f = new float[VoiceConstants.FrameSamples];
                clip.GetData(f, readPos);
                frames.Enqueue(f);
                any = true;

                readPos = (readPos + VoiceConstants.FrameSamples) % clipSamples;
                avail -= VoiceConstants.FrameSamples;
            }

            if (any) signal.Set();
        }

        void WorkerLoop()
        {
            var encoder = new OpusEncoderWrapper();
            bool speaking = false;
            int hang = 0;

            try
            {
                while (running)
                {
                    if (!frames.TryDequeue(out var f))
                    {
                        signal.WaitOne(20);
                        continue;
                    }

                    bool gate = !Muted && (!UsePushToTalk || PushToTalkHeld);
                    bool voice = gate && Rms(f) >= VadThreshold;

                    if (voice) hang = VadHang();
                    bool active = gate && (voice || hang > 0);
                    if (!voice && hang > 0) hang--;
                    if (!gate) hang = 0;

                    if (active)
                    {
                        if (!speaking)
                        {
                            speaking = true;
                            startPending = true;
                        }

                        if (bundleFrames == 0) bundleFirstSeq = nextSeq;

                        int n = encoder.Encode(f, bundle, bundleLen + 1);
                        if (n > 0)
                        {
                            bundle[bundleLen] = (byte)n;
                            bundleLen += 1 + n;
                            bundleFrames++;
                            nextSeq++;
                        }

                        if (bundleFrames >= VoiceConstants.FramesPerPacket) Flush(0);
                    }
                    else if (speaking)
                    {
                        speaking = false;
                        Flush(VoiceConstants.FlagEnd);
                    }

                    framePool.Enqueue(f);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                encoder.Dispose();
            }
        }

        int VadHang() => HangoverFrames;

        void Flush(byte extraFlags)
        {
            byte flags = extraFlags;
            if (startPending)
            {
                flags |= VoiceConstants.FlagStart;
                startPending = false;
            }

            var data = new byte[bundleLen > 0 ? bundleLen : 1];
            if (bundleLen > 0) Buffer.BlockCopy(bundle, 0, data, 0, bundleLen);

            ushort seq = bundleFrames > 0 ? bundleFirstSeq : nextSeq;
            outbox.Enqueue(new VoicePacket { Seq = seq, Flags = flags, Data = data });

            bundleLen = 0;
            bundleFrames = 0;
        }

        static float Rms(float[] f)
        {
            float sum = 0f;
            for (int i = 0; i < f.Length; i++) sum += f[i] * f[i];
            return Mathf.Sqrt(sum / f.Length);
        }
    }
}
