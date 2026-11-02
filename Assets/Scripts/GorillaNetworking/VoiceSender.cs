using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using Fusion;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace GorillaNetworking
{
    [RequireComponent(typeof(VoiceReceiver))]
    public class VoiceSender : NetworkBehaviour
    {
        public const float MaxHearDistance = 25f;

        readonly ConcurrentQueue<VoicePacket> outbox = new ConcurrentQueue<VoicePacket>();
        VoiceCapture capture;
        VoiceReceiver receiver;
        Coroutine startRoutine;

        public bool Muted
        {
            get => capture != null && capture.Muted;
            set { if (capture != null) capture.Muted = value; }
        }

        public void SetPushToTalk(bool enabled, bool held)
        {
            if (capture == null) return;
            capture.UsePushToTalk = enabled;
            capture.PushToTalkHeld = held;
        }

        public override void Spawned()
        {
            receiver = GetComponent<VoiceReceiver>();

            if (HasStateAuthority)
                startRoutine = StartCoroutine(StartCaptureWhenReady());
        }

        IEnumerator StartCaptureWhenReady()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);

                float waited = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                    waited += Time.unscaledDeltaTime;
                    if (waited > 60f)
                    {
                        Debug.LogWarning("[GorillaNetworking] Microphone permission was not granted.");
                        yield break;
                    }
                    yield return null;
                }
            }
#endif
            capture = new VoiceCapture(outbox);
            capture.Start();
            yield break;
        }

        void Update()
        {
            if (capture == null) return;
            if (!HasStateAuthority) return;

            capture.Tick();

            while (outbox.TryDequeue(out var p))
            {
                RPC_Voice(p.Seq, p.Flags, p.Data);
                Interlocked.Increment(ref VoiceStats.PacketsSent);
                Interlocked.Add(ref VoiceStats.BytesSent, p.Data.Length);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Unreliable)]
        void RPC_Voice(ushort seq, byte flags, byte[] data)
        {
            if (HasStateAuthority) return;
            if (receiver != null) receiver.Push(seq, flags, data, MaxHearDistance);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (startRoutine != null) StopCoroutine(startRoutine);
            capture?.Stop();
            capture = null;
        }
    }
}
