using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CaseClosed.TruthEngine;

namespace CaseClosed.Game
{
    /// <summary>
    /// Bridges the TruthEngine to the world: generates the case from a seed,
    /// spawns evidence into the zones its FoundAt string names, staffs the
    /// building with witnesses, runs the investigation clock. Ground truth
    /// stays inside the CaseFile - nothing here prints GUILTY anywhere.
    /// </summary>
    public class CaseRuntime : MonoBehaviour
    {
        public ulong Seed = 2;
        public float InvestigationSeconds = 900f;

        public CaseFile Case { get; private set; }
        public float TimeRemaining { get; private set; }
        public int EvidenceFound { get; private set; }
        public int EvidenceTotal { get; private set; }
        public bool BellRung { get; private set; }
        public readonly List<string> Log = new List<string>();

        public static CaseRuntime Instance { get; private set; }

        // witness index -> zone the NPC stands in (spread per GDD 04 schedules-lite)
        private static readonly string[] WitnessZones =
            { "Cafeteria", "MainHall", "EvidenceLocker", "Security", "ParkingGarage" };

        private void Awake() => Instance = this;

        /// <summary>Deterministic world-build: same seed on every client = same case.</summary>
        public void GenerateNow(ulong seed)
        {
            if (Case != null) return;
            Seed = seed;
            Case = CaseGenerator.Generate(Seed);
            TimeRemaining = InvestigationSeconds;
            EvidenceTotal = Case.Evidence.Count;

            var anchors = FindObjectsByType<ZoneAnchor>()
                .ToDictionary(a => a.ZoneName, a => a.transform);

            SpawnEvidence(anchors);
            SpawnWitnesses(anchors);

            AddLog($"CASE: {Case.Title} (seed {Case.Seed})");
            AddLog($"Charge: theft of {Case.CrimeObject}. Defendant: {Case.Defendant}.");
            AddLog("Find evidence. Question witnesses. The bell waits for no one.");
        }

        private void Update()
        {
            if (Case == null || BellRung) return;
            TimeRemaining -= Time.deltaTime;
            if (TimeRemaining <= 0f)
            {
                var nm = Unity.Netcode.NetworkManager.Singleton;
                bool online = nm != null && nm.IsListening;
                if (!online)
                    RingBell();                              // offline: ring locally
                else if (nm.IsServer)
                    CaseNetSync.Instance.ServerRingBell();   // server decides; RPC rings everyone
                else
                    TimeRemaining = 0.01f;                   // client: hold at 0 until the server's bell
            }
        }

        /// <summary>The docket bell: investigation over, everyone into the courtroom.</summary>
        public void RingBell()
        {
            if (BellRung) return;
            BellRung = true;
            TimeRemaining = 0f;
            AddLog("*** THE DOCKET BELL RINGS - investigation is over. ***");
            AddLog("The bailiff escorts everyone into Courtroom A. Court is in session.");
            TeleportLocalPlayerToCourtroom();
        }

        private void TeleportLocalPlayerToCourtroom()
        {
            var fpc = FindObjectsByType<FirstPersonController>()
                .FirstOrDefault(p => p.enabled && p.gameObject.activeInHierarchy);
            if (fpc == null) return;

            var nm = Unity.Netcode.NetworkManager.Singleton;
            int seat = nm != null && nm.IsListening ? (int)nm.LocalClientId : 0;
            var pos = new Vector3(4.5f + (seat % 4) * 3.8f, 0.15f, 7.9f + (seat / 4) * 1.5f);

            var cc = fpc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;              // CC stomps teleports; toggle around it
            fpc.transform.SetPositionAndRotation(pos, Quaternion.identity); // face the judge
            if (cc != null) cc.enabled = true;
        }

        private void SpawnEvidence(Dictionary<string, Transform> anchors)
        {
            int i = 0;
            foreach (var item in Case.Evidence)
            {
                var zone = ZoneFor(item.FoundAt, anchors);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Evidence_{i}";
                go.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
                go.transform.position = zone.position + ScatterOffset(i);
                var pickup = go.AddComponent<EvidencePickup>();
                pickup.Init(i, item.Name);
                i++;
            }
        }

        private void SpawnWitnesses(Dictionary<string, Transform> anchors)
        {
            var witnesses = Case.CastNames.Skip(1).ToList();
            for (int w = 0; w < witnesses.Count; w++)
            {
                string zoneName = WitnessZones[w % WitnessZones.Length];
                anchors.TryGetValue(zoneName, out var zone);
                if (zone == null) zone = anchors["MainHall"];

                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"Witness_{witnesses[w]}";
                go.transform.position = zone.position + ScatterOffset(w + 20) + Vector3.up * 1.0f;
                var npc = go.AddComponent<WitnessNpc>();
                npc.Init(witnesses[w], KitWriter.OpeningStatement(Case, witnesses[w]));
            }
        }

        private static Transform ZoneFor(string foundAt, Dictionary<string, Transform> anchors)
        {
            string key =
                foundAt.Contains("parking") || foundAt.Contains("Impound") ? "ParkingGarage" :
                foundAt.Contains("Lab") ? "Lab" :
                foundAt.Contains("Security") ? "Security" :
                foundAt.Contains("Archives") ? "Archives" : "MainHall";
            return anchors.TryGetValue(key, out var t) ? t : anchors["MainHall"];
        }

        private static Vector3 ScatterOffset(int i)
        {
            float angle = i * 2.399f; // golden-angle scatter, deterministic
            float r = 1.2f + (i % 3) * 0.8f;
            return new Vector3(Mathf.Cos(angle) * r, 0.35f, Mathf.Sin(angle) * r);
        }

        /// <summary>Applies a (validated) collection locally - called on every client.</summary>
        public void ApplyCollect(int index, string itemName)
        {
            if (BellRung) { AddLog("The bell has rung. Nothing more can be collected."); return; }
            var go = GameObject.Find($"Evidence_{index}");
            if (go == null) return;
            Destroy(go);
            EvidenceFound++;
            AddLog($"+ Collected: {itemName}  ({EvidenceFound}/{EvidenceTotal})");
        }

        public void AddLog(string line)
        {
            Log.Add(line);
            if (Log.Count > 60) Log.RemoveAt(0);
        }
    }
}

