using System.Threading;
using UnityEngine;

namespace GorillaNetworking
{
    public class VoiceStatsLogger : MonoBehaviour
    {
        float next;

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;

            int ps = Interlocked.Exchange(ref VoiceStats.PacketsSent, 0);
            int bs = Interlocked.Exchange(ref VoiceStats.BytesSent, 0);
            int pr = Interlocked.Exchange(ref VoiceStats.PacketsReceived, 0);
            int br = Interlocked.Exchange(ref VoiceStats.BytesReceived, 0);
            int pd = Interlocked.Exchange(ref VoiceStats.PacketsDropped, 0);

            if (ps + pr + pd == 0) return;
            Debug.Log($"[GorillaNetworking] tx {ps} pkt/s ({bs} B/s) | rx {pr} pkt/s ({br} B/s) | dropped {pd}");
        }
    }
}
