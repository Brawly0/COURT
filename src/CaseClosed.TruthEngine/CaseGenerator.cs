using System;
using System.Collections.Generic;
using System.Linq;

namespace CaseClosed.TruthEngine
{
    /// <summary>
    /// The Truth Engine pipeline (GDD 06). Generates a complete case from a
    /// seed, enforcing the invariants in code, not in hope:
    ///   - pooled-solvable: >= MinProofFacts independent facts implicate the perp
    ///   - detectability: every corruption has a discoverable counter, or it is reverted
    ///   - Baggage Rule: always 2-3 innocent-but-incriminating defendant facts
    ///   - stamp contract: CLEAR fragments lie only in Fractured cases
    ///   - the defendant never receives a scripted lie (Clarity = the player's choice)
    /// Deterministic: same seed, same case, any platform (Pcg32).
    /// </summary>
    public static class CaseGenerator
    {
        public static CaseFile Generate(ulong seed)
        {
            var rng = new Pcg32(seed);
            CaseFile last = null;
            for (int attempt = 0; attempt <= World.MaxRerolls; attempt++)
            {
                var c = BuildOnce(seed, rng);
                c.Rerolls = attempt;
                if (c.ProofFacts.Count >= World.MinProofFacts) return c;
                last = c;
            }
            return last; // pathological guard; validate command reports these
        }

        private static CaseFile BuildOnce(ulong seed, Pcg32 rng)
        {
            var c = new CaseFile { Seed = seed };
            int nChars = World.Cast.Length;
            int nSlots = World.Slots.Length;
            c.CastNames.AddRange(World.Cast);
            c.Defendant = World.Cast[0];

            var pickObj = rng.Pick(World.CrimeObjects);
            c.CrimeObject = pickObj.Object;
            c.Title = pickObj.Title;

            // ---- Stage 2: occupancy matrix (constrained random walk) ----
            c.Occupancy = new int[nChars][];
            for (int i = 0; i < nChars; i++)
            {
                var row = new int[nSlots];
                int pos = rng.Next(World.Locations.Length);
                for (int t = 0; t < nSlots; t++)
                {
                    var moves = World.Adjacency[pos].Append(pos).ToArray();
                    pos = moves[rng.Next(moves.Length)];
                    row[t] = pos;
                }
                c.Occupancy[i] = row;
            }

            // ---- crime ----
            c.Guilty = rng.Chance(World.GuiltPrior);
            c.CrimeSlot = rng.Next(1, nSlots - 1);
            c.Perpetrator = c.Guilty ? c.Defendant : World.Cast[rng.Next(1, nChars)];
            c.CrimeLocation = c.Occupancy[c.CastIndex(c.Perpetrator)][c.CrimeSlot];
            c.Clarity = RollClarity(rng);

            // CCTV corridor: covers transits from one neighbor into l*
            int camNeighbor = rng.Pick(World.Adjacency[c.CrimeLocation]);

            // ---- Stage 5: observations Obs(X) ----
            for (int i = 0; i < nChars; i++)
            {
                var who = World.Cast[i];
                var list = new List<Observation>();
                for (int t = 0; t < nSlots; t++)
                    for (int j = 0; j < nChars; j++)
                        if (j != i && c.Occupancy[j][t] == c.Occupancy[i][t])
                            list.Add(new Observation("saw", World.Cast[j], c.Occupancy[j][t], t));
                if (World.Adjacency[c.CrimeLocation].Contains(c.Occupancy[i][c.CrimeSlot]))
                    list.Add(new Observation("heard", "a commotion", c.CrimeLocation, c.CrimeSlot));
                c.Obs[who] = list;
            }

            // ---- memory corruption (witnesses only; they believe all of it) ----
            for (int i = 1; i < nChars; i++)
                CorruptWitness(c, World.Cast[i], rng);

            // ---- protector agenda: someone covers for the perp ----
            ApplyProtector(c, rng);

            // ---- culprit self-preservation (witness perps only; NEVER the defendant) ----
            ApplyPerpSelfLie(c, rng, camNeighbor);

            // ---- Stage 3b: the Baggage Rule (non-negotiable) ----
            BuildBaggage(c, rng, nSlots);

            // ---- evidence derivation & courthouse placement ----
            BuildEvidence(c, camNeighbor, nSlots);

            // ---- Stage 5b: the defendant's hand (Clarity spectrum) ----
            BuildHand(c, rng, nSlots);

            // ---- Stage 8: pooled-solvable proof chain ----
            BuildProofFacts(c, camNeighbor);
            return c;
        }

        private static Clarity RollClarity(Pcg32 rng)
        {
            double r = rng.NextDouble();
            if (r < World.ClarityLucid) return Clarity.Lucid;
            if (r < World.ClarityLucid + World.ClarityHazy) return Clarity.Hazy;
            return Clarity.Fractured;
        }

        // ------------------------------------------------------------------
        private static void CorruptWitness(CaseFile c, string witness, Pcg32 rng)
        {
            int wi = c.CastIndex(witness);
            int count = rng.Next(1, 4);
            for (int k = 0; k < count; k++)
            {
                var kind = (CorruptionKind)rng.Next(3); // TimeShift | DescriptorSwap | InferencePromotion
                for (int attempt = 0; attempt < 4; attempt++)
                    if (TryApplyCorruption(c, witness, wi, kind, rng)) break;
            }
        }

        private static bool TryApplyCorruption(CaseFile c, string witness, int wi, CorruptionKind kind, Pcg32 rng)
        {
            var sawObs = c.Obs[witness].Where(o => o.Verb == "saw" && !o.Corrupted).ToList();

            if (kind == CorruptionKind.InferencePromotion)
            {
                // Fabricate "I saw Y in L at T" where the witness was only ADJACENT:
                // content is true about Y, but direct sight was impossible (hearsay-vulnerable).
                var candidates = new List<(string y, int loc, int t)>();
                for (int t = 0; t < World.Slots.Length; t++)
                {
                    int myLoc = c.Occupancy[wi][t];
                    for (int j = 0; j < World.Cast.Length; j++)
                    {
                        if (j == wi) continue;
                        int theirLoc = c.Occupancy[j][t];
                        if (World.Adjacency[myLoc].Contains(theirLoc))
                            candidates.Add((World.Cast[j], theirLoc, t));
                    }
                }
                if (candidates.Count == 0) return false;
                var pick = rng.Pick(candidates);
                var fake = new Observation("saw", pick.y, pick.loc, pick.t) { Corrupted = true };
                c.Obs[witness].Add(fake);
                c.Ledger.Add(new CorruptionEntry(witness, CorruptionKind.InferencePromotion,
                    $"was in the {World.Locations[c.Occupancy[wi][pick.t]]} at {World.Slots[pick.t]} — could only HEAR the {World.Locations[pick.loc]}, never saw it",
                    $"place {witness} at {World.Slots[pick.t]} via others' sightings or the matrix — direct sight was impossible (hearsay)"));
                return true;
            }

            if (sawObs.Count == 0) return false;
            var target = rng.Pick(sawObs);
            var original = target.Copy();

            if (kind == CorruptionKind.TimeShift)
            {
                int shift = target.Slot == 0 ? 1
                          : target.Slot == World.Slots.Length - 1 ? -1
                          : (rng.Chance(0.5) ? 1 : -1);
                target.Slot += shift;
            }
            else // DescriptorSwap
            {
                var others = World.Cast.Where(n => n != witness && n != target.Subject).ToList();
                target.Subject = rng.Pick(others);
            }

            // if the mutated claim is accidentally true, it is not a lie — undo, try again
            int subjIdx = c.CastIndex(target.Subject);
            if (c.Occupancy[subjIdx][target.Slot] == target.Location)
            { Restore(target, original); return false; }

            string counter = FindCounter(c, witness, target);
            if (counter == null)
            { Restore(target, original); c.RerolledCorruptions++; return false; }

            target.Corrupted = true;
            c.Ledger.Add(new CorruptionEntry(witness, kind,
                $"actually saw {original.Subject} in the {World.Locations[original.Location]} at {World.Slots[original.Slot]}",
                counter));
            return true;
        }

        private static void Restore(Observation target, Observation original)
        {
            target.Verb = original.Verb; target.Subject = original.Subject;
            target.Location = original.Location; target.Slot = original.Slot;
            target.Corrupted = original.Corrupted;
        }

        /// <summary>Detectability invariant: a discoverable fact that disproves the false claim.</summary>
        private static string FindCounter(CaseFile c, string claimant, Observation claim)
        {
            int subjIdx = c.CastIndex(claim.Subject);
            int trueLoc = c.Occupancy[subjIdx][claim.Slot];

            foreach (var kv in c.Obs)
            {
                if (kv.Key == claimant) continue;
                if (kv.Value.Any(o => o.Verb == "saw" && !o.Corrupted &&
                                      o.Subject == claim.Subject && o.Slot == claim.Slot && o.Location == trueLoc))
                    return $"{kv.Key} truly saw {claim.Subject} in the {World.Locations[trueLoc]} at {World.Slots[claim.Slot]}";
            }
            bool subjectIsLyingPerp = claim.Subject == c.Perpetrator && claim.Slot == c.CrimeSlot
                                      && c.PerpClaimedLocation >= 0;
            if (!subjectIsLyingPerp)
                return $"{claim.Subject}'s own account places them in the {World.Locations[trueLoc]} at {World.Slots[claim.Slot]}";
            if (trueLoc == c.CrimeLocation)
                return $"the {World.Locations[c.CrimeLocation]} door log at {World.Slots[claim.Slot]}";
            return null;
        }

        // ------------------------------------------------------------------
        private static void ApplyProtector(CaseFile c, Pcg32 rng)
        {
            foreach (var name in World.Cast.Skip(1))
            {
                if (name == c.Perpetrator) continue;
                var hits = c.Obs[name].Where(o => o.Verb == "saw" && !o.Corrupted &&
                                                 o.Subject == c.Perpetrator &&
                                                 o.Slot == c.CrimeSlot &&
                                                 o.Location == c.CrimeLocation).ToList();
                if (hits.Count > 0 && rng.Chance(0.6))
                {
                    c.Protector = name;
                    foreach (var h in hits) c.Obs[name].Remove(h);
                    c.Ledger.Add(new CorruptionEntry(name, CorruptionKind.ProtectAgenda,
                        $"deleted their sighting of {c.Perpetrator} at the {World.Locations[c.CrimeLocation]} at {World.Slots[c.CrimeSlot]} (protecting them)",
                        "cross-reference: door log, CCTV, and other witnesses at the scene"));
                    return;
                }
            }
        }

        private static void ApplyPerpSelfLie(CaseFile c, Pcg32 rng, int camNeighbor)
        {
            if (c.Perpetrator == c.Defendant) return; // the defendant's lies are the PLAYER's choice
            if (!rng.Chance(World.PerpSelfLieChance)) return;

            bool counterExists =
                DoorLogPlacesPerp(c) || CctvPlacesPerp(c, camNeighbor) ||
                c.Obs.Where(kv => kv.Key != c.Perpetrator).Any(kv =>
                    kv.Value.Any(o => o.Verb == "saw" && !o.Corrupted &&
                                      o.Subject == c.Perpetrator &&
                                      o.Slot == c.CrimeSlot && o.Location == c.CrimeLocation));
            if (!counterExists) return; // an uncatchable lie violates detectability — skip

            var fakeLocs = Enumerable.Range(0, World.Locations.Length)
                                     .Where(l => l != c.CrimeLocation).ToList();
            c.PerpClaimedLocation = rng.Pick(fakeLocs);
            c.Obs[c.Perpetrator].RemoveAll(o => o.Verb == "saw" && o.Slot == c.CrimeSlot);
            c.Ledger.Add(new CorruptionEntry(c.Perpetrator, CorruptionKind.SelfPreservation,
                $"claims the {World.Locations[c.PerpClaimedLocation]} at {World.Slots[c.CrimeSlot]} — was at the {World.Locations[c.CrimeLocation]} (the crime); also denies seeing anyone there",
                "door log, CCTV, and honest witnesses at the scene"));
        }

        // ------------------------------------------------------------------
        private static void BuildBaggage(CaseFile c, Pcg32 rng, int nSlots)
        {
            int tBag = Math.Max(0, c.CrimeSlot - rng.Next(1, 3));
            int lieSlot = rng.Next(nSlots);
            string reason = rng.Pick(World.EmbarrassingReasons);
            int trueLoc = c.Occupancy[0][lieSlot];
            var lieLocs = Enumerable.Range(0, World.Locations.Length).Where(l => l != trueLoc).ToList();
            int lieLoc = rng.Pick(lieLocs);

            c.Baggage.Add($"{c.Defendant}'s prints are on {c.CrimeObject} (they moved it at {World.Slots[tBag]} — routine, but nobody asked)");
            c.Baggage.Add($"{c.Defendant} told police they were in the {World.Locations[lieLoc]} at {World.Slots[lieSlot]} — actually in the {World.Locations[trueLoc]}, {reason} (the Secret)");
            c.Baggage.Add($"A guest saw {c.Defendant} leaving in a hurry at {World.Slots[Math.Min(c.CrimeSlot + 1, nSlots - 1)]}");

            c.SecretText = $"you were {reason} at {World.Slots[lieSlot]} and lied to police about it";
        }

        private static void BuildEvidence(CaseFile c, int camNeighbor, int nSlots)
        {
            var handlers = new List<string> { c.Defendant };
            if (!handlers.Contains(c.Perpetrator)) handlers.Add(c.Perpetrator);

            var transits = new List<string>();
            for (int i = 0; i < World.Cast.Length; i++)
                for (int t = 1; t < nSlots; t++)
                    if (c.Occupancy[i][t] == c.CrimeLocation && c.Occupancy[i][t - 1] == camNeighbor)
                        transits.Add($"{World.Cast[i]} passed {World.Locations[camNeighbor]} -> {World.Locations[c.CrimeLocation]} at {World.Slots[t]}");

            var arrivals = new List<string>();
            for (int t = 0; t < nSlots; t++)
                for (int i = 0; i < World.Cast.Length; i++)
                    if (c.Occupancy[i][t] == c.CrimeLocation && (t == 0 || c.Occupancy[i][t - 1] != c.CrimeLocation))
                        arrivals.Add($"{World.Cast[i]} at {World.Slots[t]}");

            c.Evidence.Add(new EvidenceItem($"{c.CrimeObject} (recovered)", "Impound cage, parking garage",
                $"recovered near the {World.Locations[c.CrimeLocation]}"));
            c.Evidence.Add(new EvidenceItem($"Fingerprint card: {string.Join(", ", handlers)}",
                "Lab tray (processing: 90s)", "handlers listed in touch order"));
            c.Evidence.Add(new EvidenceItem($"CCTV tape: corridor {World.Locations[camNeighbor]} -> {World.Locations[c.CrimeLocation]} ({transits.Count} entries)",
                "Security office console", transits.Count > 0 ? string.Join("; ", transits) : "no entries"));
            c.Evidence.Add(new EvidenceItem("Catering schedule (places staff by slot)", "Archives, row 3",
                "corroborates staff members' true movements"));
            c.Evidence.Add(new EvidenceItem($"Door log: {World.Locations[c.CrimeLocation]}", "Archives, row 5",
                arrivals.Count > 0 ? string.Join("; ", arrivals) : "no entries"));
            if (c.Clarity == Clarity.Fractured)
                c.Evidence.Add(new EvidenceItem("Toxicology slip (defendant's blood sample)", "Lab tray (processing: 120s)",
                    "sedative present — the defendant's confident memories are chemically unreliable"));
        }

        private static void BuildHand(CaseFile c, Pcg32 rng, int nSlots)
        {
            bool anyCorrupted = false;
            for (int t = 0; t < nSlots; t++)
            {
                var stamp = rng.Chance(World.ClearStampRate) ? Stamp.Clear : Stamp.Hazy;
                int loc = c.Occupancy[0][t];
                var fidelity = Fidelity.Reliable;
                string cause = null;
                if (c.Clarity == Clarity.Fractured && stamp == Stamp.Clear && rng.Chance(0.5))
                {
                    var wrong = Enumerable.Range(0, World.Locations.Length).Where(l => l != loc).ToList();
                    loc = rng.Pick(wrong);
                    fidelity = Fidelity.Corrupted;
                    cause = "spiked drink — the toxicology slip at the Lab proves it";
                    anyCorrupted = true;
                }
                var verb = stamp == Stamp.Clear ? "You remember" : "You think you remember";
                c.Hand.Add(new MemoryFragment(stamp, $"{verb}: being in the {World.Locations[loc]} at {World.Slots[t]}", fidelity, cause));
            }

            // stamp contract: Fractured must contain >= 1 false CLEAR; others contain none (by construction)
            if (c.Clarity == Clarity.Fractured && !anyCorrupted)
            {
                var frag = c.Hand[rng.Next(c.Hand.Count)];
                int t = c.Hand.IndexOf(frag);
                int trueLoc = c.Occupancy[0][t];
                var wrong = Enumerable.Range(0, World.Locations.Length).Where(l => l != trueLoc).ToList();
                c.Hand[t] = new MemoryFragment(Stamp.Clear,
                    $"You remember: being in the {World.Locations[rng.Pick(wrong)]} at {World.Slots[t]}",
                    Fidelity.Corrupted, "spiked drink — the toxicology slip at the Lab proves it");
            }

            if (c.Clarity == Clarity.Lucid)
            {
                string decisive = c.Guilty ? $"taking {c.CrimeObject}" : $"NOT taking {c.CrimeObject}";
                c.Hand.Add(new MemoryFragment(Stamp.Clear,
                    $"You remember: {decisive} at {World.Slots[c.CrimeSlot]}", Fidelity.Reliable));
            }
            c.Hand.Add(new MemoryFragment(Stamp.Clear, $"Secret: {c.SecretText}", Fidelity.Reliable));
        }

        // ------------------------------------------------------------------
        private static bool DoorLogPlacesPerp(CaseFile c)
        {
            int pi = c.CastIndex(c.Perpetrator);
            for (int t = 0; t <= c.CrimeSlot; t++)
            {
                bool arrived = c.Occupancy[pi][t] == c.CrimeLocation && (t == 0 || c.Occupancy[pi][t - 1] != c.CrimeLocation);
                if (!arrived) continue;
                bool stayed = true;
                for (int u = t; u <= c.CrimeSlot; u++)
                    if (c.Occupancy[pi][u] != c.CrimeLocation) { stayed = false; break; }
                if (stayed) return true;
            }
            return false;
        }

        private static bool CctvPlacesPerp(CaseFile c, int camNeighbor)
        {
            int pi = c.CastIndex(c.Perpetrator);
            return c.CrimeSlot >= 1 &&
                   c.Occupancy[pi][c.CrimeSlot] == c.CrimeLocation &&
                   c.Occupancy[pi][c.CrimeSlot - 1] == camNeighbor;
        }

        private static void BuildProofFacts(CaseFile c, int camNeighbor)
        {
            c.ProofFacts.Add($"prints: {c.Perpetrator} handled {c.CrimeObject}");
            if (DoorLogPlacesPerp(c))
                c.ProofFacts.Add($"door log: {c.Perpetrator} inside the {World.Locations[c.CrimeLocation]} through {World.Slots[c.CrimeSlot]}");
            if (CctvPlacesPerp(c, camNeighbor))
                c.ProofFacts.Add($"CCTV: {c.Perpetrator} entered the {World.Locations[c.CrimeLocation]} at {World.Slots[c.CrimeSlot]}");
            foreach (var kv in c.Obs)
            {
                if (kv.Key == c.Perpetrator) continue;
                if (kv.Value.Any(o => o.Verb == "saw" && !o.Corrupted &&
                                      o.Subject == c.Perpetrator &&
                                      o.Slot == c.CrimeSlot && o.Location == c.CrimeLocation))
                    c.ProofFacts.Add($"witness: {kv.Key} saw {c.Perpetrator} at the scene");
            }
        }
    }
}
