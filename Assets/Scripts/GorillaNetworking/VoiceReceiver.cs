using System.Collections.Generic;
using System.Threading;
using Fusion;
using UnityEngine;

namespace GorillaNetworking
{
    public class VoiceReceiver : NetworkBehaviour
    {
        public static readonly HashSet<PlayerRef> Blocked = new HashSet<PlayerRef>();

        public static Transform ListenerOverride;

        const float MaxPacketsPerSecond = 25f;
        const float BurstPackets = 10f;

        [SerializeField] VoicePlayback playback;

        public bool Muted;

        VoiceJitterBuffer jitter;
        Transform listener;
        float tokens = BurstPackets;
        float lastRefill;

        public override void Spawned()
        {
            if (HasStateAuthority) return;

            if (playback == null) playback = GetComponentInChildren<VoicePlayback>();

            jitter = new VoiceJitterBuffer();
            lastRefill = Time.unscaledTime;

            if (playback != null) playback.Attach(jitter);
            else Debug.LogError("[GorillaNetworking] VoiceReceiver has no VoicePlayback assigned.");
        }

        public void Push(ushort seq, byte flags, byte[] data, float maxDist)
        {
            if (jitter == null) return;

            if (data == null || data.Length == 0 || data.Length > VoiceConstants.MaxPacketBytes)
            {
                Interlocked.Increment(ref VoiceStats.PacketsDropped);
                return;
            }

            if (Muted || Blocked.Contains(Object.StateAuthority))
            {
                Interlocked.Increment(ref VoiceStats.PacketsDropped);
                return;
            }

            float now = Time.unscaledTime;
            tokens = Mathf.Min(BurstPackets, tokens + (now - lastRefill) * MaxPacketsPerSecond);
            lastRefill = now;
            if (tokens < 1f)
            {
                Interlocked.Increment(ref VoiceStats.PacketsDropped);
                return;
            }
            tokens -= 1f;

            if (listener == null)
            {
                if (ListenerOverride != null) listener = ListenerOverride;
                else if (Camera.main != null) listener = Camera.main.transform;
                else return;
            }
            if ((transform.position - listener.position).sqrMagnitude > maxDist * maxDist) return;

            Interlocked.Increment(ref VoiceStats.PacketsReceived);
            Interlocked.Add(ref VoiceStats.BytesReceived, data.Length);

            jitter.Push(seq, flags, data);
        }

        public int BufferedFrames => jitter != null ? jitter.Depth : 0;

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (playback != null) playback.Detach();
            jitter?.Dispose();
            jitter = null;
        }
    }
}
