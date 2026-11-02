using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GorillaNetworking
{
    public class VoiceLoopbackTest : MonoBehaviour
    {
        [SerializeField] VoicePlayback playback;
        [Range(0f, 0.5f)] public float packetLoss = 0.1f;
        public float baseDelayMs = 40f;
        public float jitterMs = 30f;

        struct Pending
        {
            public VoicePacket Packet;
            public float Due;
        }

        readonly ConcurrentQueue<VoicePacket> outbox = new ConcurrentQueue<VoicePacket>();
        readonly List<Pending> pending = new List<Pending>();
        VoiceCapture capture;
        VoiceJitterBuffer jitter;

        IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                while (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) yield return null;
            }
#endif
            if (playback == null) playback = GetComponent<VoicePlayback>();

            jitter = new VoiceJitterBuffer();
            playback.Attach(jitter);

            capture = new VoiceCapture(outbox);
            capture.Start();
            yield break;
        }

        void Update()
        {
            if (capture == null) return;

            capture.Tick();

            while (outbox.TryDequeue(out var p))
            {
                if (Random.value < packetLoss) continue;

                float delay = (baseDelayMs + Random.Range(0f, jitterMs)) / 1000f;
                pending.Add(new Pending { Packet = p, Due = Time.unscaledTime + delay });
            }
            
            float now = Time.unscaledTime;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Due > now) continue;
                var p = pending[i].Packet;
                pending.RemoveAt(i);
                jitter.Push(p.Seq, p.Flags, p.Data);
            }
        }

        void OnDestroy()
        {
            capture?.Stop();
            if (playback != null) playback.Detach();
            jitter?.Dispose();
        }
    }
}
