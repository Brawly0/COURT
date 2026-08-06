using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CaseClosed.Game.Prototype.Voice
{
    /// <summary>
    /// WHY THIS EXISTS: the bridge between a microphone and everyone else's ears.
    /// It owns the network side and nothing else — capture and playback are separate
    /// components that know nothing about NGO.
    ///
    /// THE ROUTE A FRAME TAKES
    ///   1. VoiceCapture raises FrameReady on the speaker's machine.
    ///   2. SendVoiceServerRpc carries it to the host.
    ///   3. The host measures speaker-to-listener distance for every connected
    ///      client and builds a target list of those in range.
    ///   4. ReceiveVoiceClientRpc delivers only to that list.
    ///   5. Each recipient hands the frame to VoicePlayback on the SPEAKER'S
    ///      character, so the sound comes out of the right body.
    ///
    /// Culling on the server, not the client, is the important part. It is the hard
    /// range limit — a modified client cannot listen in on a conversation across the
    /// map, because those packets were never sent to it. It also keeps host
    /// bandwidth proportional to who can actually hear whom.
    ///
    /// Courtroom rules later ("only the witness may be heard") slot into
    /// BuildListenerList without touching capture, playback or movement.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerVoice : NetworkBehaviour
    {
        [Header("Proximity")]
        [Tooltip("Beyond this many metres a player cannot be heard at all. Enforced on the server.")]
        public float MaxVoiceDistance = 18f;

        [Tooltip("Volume at maximum range. 0 = fades to nothing. Raise for a floor of audibility.")]
        [Range(0f, 1f)] public float MinVoiceVolume = 0f;

        [Tooltip("Volume against distance, sampled from 0 (touching) to 1 (max range).")]
        public AnimationCurve FalloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Tooltip("Extra metres of slack on the server's range test, so audio does not cut " +
                 "out abruptly the instant someone steps over the line.")]
        public float CullingMargin = 3f;

        [Header("Occlusion")]
        [Tooltip("What counts as a wall. Leave as Everything; player bodies are skipped automatically.")]
        public LayerMask OcclusionLayers = ~0;

        [Tooltip("How many surfaces in the way count as fully blocked. 1 = a single wall " +
                 "silences; 3 = you can still faintly hear through one wall.")]
        [Range(1, 6)] public int BlockersForFullOcclusion = 3;

        [Tooltip("Voice range multiplier when fully walled off. 0.35 = you must be about " +
                 "a third as far away to be heard at all through walls.")]
        [Range(0.05f, 1f)] public float OccludedRangeMultiplier = 0.35f;

        [Tooltip("Seconds between line-of-sight checks. Raycasting every packet would be " +
                 "50 casts a second per listener for no benefit - people do not move that fast.")]
        [Range(0.03f, 1f)] public float OcclusionRefreshSeconds = 0.12f;

        [Tooltip("Height above the character's feet treated as the mouth/ear, so rays are not " +
                 "fired along the floor.")]
        public float HeadHeight = 1.5f;

        [Header("References")]
        public VoiceCapture Capture;
        public VoicePlayback Playback;

        /// <summary>
        /// Whether this player is audibly speaking, FROM THIS MACHINE'S POINT OF VIEW.
        ///
        /// Deliberately not a NetworkVariable. Replicating it told every client that a
        /// player across the map was talking even when the server had correctly refused
        /// to send them the audio — which in a game built on eavesdropping is exactly
        /// the information you must not leak.
        ///
        /// Deriving it from audio you actually received closes that hole and costs zero
        /// bandwidth: if you can hear them, you know; if you cannot, you do not.
        /// </summary>
        public bool IsSpeaking => IsOwner
            ? Capture != null && Capture.IsTransmitting
            : Playback != null && Playback.IsPlaying;

        /// <summary>How blocked this speaker is from the local listener. 0 = clear, 1 = walled off.</summary>
        public float Occlusion => Playback != null ? Playback.Occlusion : 0f;

        private float _occlusionTimer;
        private Transform _listener;

        // Server-side line-of-sight results, refreshed on a timer rather than per packet.
        private readonly Dictionary<ulong, float> _serverOcclusion = new();
        private readonly Dictionary<ulong, float> _serverOcclusionTime = new();

        /// <summary>Local mic level, owner only. 0 on remote copies.</summary>
        public float LocalInputLevel => Capture != null ? Capture.InputLevel : 0f;

        public bool IsMuted
        {
            get => Capture != null && Capture.Muted;
            set { if (Capture != null) Capture.Muted = value; }
        }

        private static readonly List<ulong> _listeners = new();

        public override void OnNetworkSpawn()
        {
            if (Capture == null) Capture = GetComponent<VoiceCapture>();
            if (Playback == null) Playback = GetComponent<VoicePlayback>();

            if (Playback != null)
                Playback.ConfigureSpatial(MaxVoiceDistance, MinVoiceVolume, FalloffCurve);

            if (IsOwner)
            {
                // Only your own machine records. Remote copies keep the component
                // switched off so six players do not open six microphones.
                if (Capture != null)
                {
                    Capture.enabled = true;
                    Capture.FrameReady += OnLocalFrameReady;
                }

                // Never play your own voice back at yourself.
                if (Playback != null) Playback.enabled = false;
                var source = GetComponent<AudioSource>();
                if (source != null) source.enabled = false;
            }
            else
            {
                if (Capture != null) Capture.enabled = false;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && Capture != null) Capture.FrameReady -= OnLocalFrameReady;
        }

        /// <summary>
        /// Client side: measure how much building sits between the local listener and
        /// this speaker, and hand it to playback for volume + muffling.
        ///
        /// Measured from the CAMERA, because that is where Unity's AudioListener rides —
        /// so what we compute matches what the ears actually are.
        /// </summary>
        private void Update()
        {
            if (IsOwner || Playback == null || !Playback.enabled) return;

            _occlusionTimer -= Time.deltaTime;
            if (_occlusionTimer > 0f) return;
            _occlusionTimer = OcclusionRefreshSeconds;

            if (_listener == null)
            {
                var camera = Camera.main;
                if (camera == null) return;
                _listener = camera.transform;
            }

            float occlusion = VoiceOcclusion.Sample(
                _listener.position,
                transform.position + Vector3.up * HeadHeight,
                OcclusionLayers, BlockersForFullOcclusion);

            Playback.SetOcclusion(occlusion);
        }

        private void OnLocalFrameReady(byte[] frame)
        {
            if (!IsSpawned) return;
            SendVoiceServerRpc(frame);
        }

        // ------------------------------------------------------------------
        // network
        // ------------------------------------------------------------------

        /// <summary>
        /// Unreliable on purpose. Voice is only useful if it is fresh — a packet
        /// re-sent 200 ms late is worse than the small gap it would have filled, and
        /// reliable delivery would head-of-line block the whole stream behind it.
        /// </summary>
        // RequireOwnership defaults to true and naming it explicitly is deprecated in
        // NGO 2.x, but the guarantee still holds: only the owner may send.
        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        private void SendVoiceServerRpc(byte[] frame, ServerRpcParams serverParams = default)
        {
            ulong speaker = serverParams.Receive.SenderClientId;
            BuildListenerList(speaker);
            if (_listeners.Count == 0) return;

            ReceiveVoiceClientRpc(frame, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = _listeners.ToArray() }
            });
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void ReceiveVoiceClientRpc(byte[] frame, ClientRpcParams clientParams = default)
        {
            // Belt and braces: the host is also a client and would otherwise hear itself.
            if (IsOwner) return;
            if (Playback != null && Playback.enabled) Playback.PushFrame(frame);
        }

        /// <summary>
        /// SERVER ONLY. Everyone close enough to hear the speaker.
        /// This is the single place proximity is enforced, and the natural home for
        /// courtroom rules later (a judge who is always heard, a gagged defendant
        /// who never is).
        /// </summary>
        private void BuildListenerList(ulong speakerClientId)
        {
            _listeners.Clear();

            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer) return;

            if (!manager.ConnectedClients.TryGetValue(speakerClientId, out var speakerClient)) return;
            var speakerObject = speakerClient.PlayerObject;
            if (speakerObject == null) return;

            Vector3 speakerPosition = speakerObject.transform.position;
            Vector3 speakerHead = speakerPosition + Vector3.up * HeadHeight;
            float openRange = MaxVoiceDistance + CullingMargin;

            foreach (var pair in manager.ConnectedClients)
            {
                if (pair.Key == speakerClientId) continue;   // never echo back

                var listenerObject = pair.Value.PlayerObject;
                if (listenerObject == null) continue;

                Vector3 listenerPosition = listenerObject.transform.position;

                // Cheap test first: nobody beyond the open-air range can hear regardless
                // of geometry, so reject them before spending a raycast.
                float distanceSquared = (listenerPosition - speakerPosition).sqrMagnitude;
                if (distanceSquared > openRange * openRange) continue;

                // Walls shrink how far a voice carries. This is the authoritative half
                // of occlusion: a modified client cannot listen through a wall from
                // across a room, because those packets are never sent to it.
                float occlusion = ServerOcclusion(pair.Key, speakerHead,
                    listenerPosition + Vector3.up * HeadHeight);

                float effectiveRange = openRange * Mathf.Lerp(1f, OccludedRangeMultiplier, occlusion);
                if (distanceSquared <= effectiveRange * effectiveRange)
                    _listeners.Add(pair.Key);
            }
        }

        /// <summary>
        /// Line-of-sight for one listener, cached on a timer.
        ///
        /// Without the cache this would fire a raycast per listener per packet — 50 a
        /// second each, 1500/s in a six-player game — to answer a question whose answer
        /// barely changes between frames.
        /// </summary>
        private float ServerOcclusion(ulong listenerClientId, Vector3 speakerHead, Vector3 listenerHead)
        {
            float now = Time.time;

            if (_serverOcclusionTime.TryGetValue(listenerClientId, out float sampledAt) &&
                now - sampledAt < OcclusionRefreshSeconds &&
                _serverOcclusion.TryGetValue(listenerClientId, out float cached))
            {
                return cached;
            }

            float occlusion = VoiceOcclusion.Sample(
                listenerHead, speakerHead, OcclusionLayers, BlockersForFullOcclusion);

            _serverOcclusion[listenerClientId] = occlusion;
            _serverOcclusionTime[listenerClientId] = now;
            return occlusion;
        }
    }
}
