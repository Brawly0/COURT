using System;
using System.Collections.Generic;
using System.Linq;
using CaseClosed.TruthEngine;

// CASE CLOSED - Truth Engine console (Phase 1 deliverable).
// Commands:
//   generate <seed>          print a full case (GM view) to stdout
//   kit <seed> <outDir>      write the Gate 0.5 paper kit (GM sheet + team handouts)
//   scan <from> <to>         one summary line per seed (for picking playtest kits)
//   validate <n>             invariant sweep over n seeds

if (args.Length == 0) { PrintHelp(); return 1; }

switch (args[0].ToLowerInvariant())
{
    case "generate":
    {
        var c = CaseGenerator.Generate(ulong.Parse(args[1]));
        Console.WriteLine(KitWriter.RenderConsole(c));
        return 0;
    }
    case "kit":
    {
        var c = CaseGenerator.Generate(ulong.Parse(args[1]));
        KitWriter.WriteKit(c, args[2]);
        Console.WriteLine($"Kit written to {args[2]}  ({KitWriter.ScanLine(c).Trim()})");
        return 0;
    }
    case "scan":
    {
        for (ulong s = ulong.Parse(args[1]); s <= ulong.Parse(args[2]); s++)
            Console.WriteLine(KitWriter.ScanLine(CaseGenerator.Generate(s)));
        return 0;
    }
    case "validate":
    {
        int n = int.Parse(args[1]);
        int guilty = 0, perpLies = 0, protectors = 0, rerollsTotal = 0, hardFails = 0;
        int revertedCorruptions = 0, ledgerTotal = 0;
        var clarity = new Dictionary<Clarity, int>();
        var proofHist = new Dictionary<int, int>();
        for (ulong s = 1; s <= (ulong)n; s++)
        {
            var c = CaseGenerator.Generate(s);
            guilty += c.Guilty ? 1 : 0;
            perpLies += c.PerpClaimedLocation >= 0 ? 1 : 0;
            protectors += c.Protector != null ? 1 : 0;
            rerollsTotal += c.Rerolls;
            revertedCorruptions += c.RerolledCorruptions;
            ledgerTotal += c.Ledger.Count;
            if (c.ProofFacts.Count < World.MinProofFacts) hardFails++;
            clarity.TryGetValue(c.Clarity, out int cc); clarity[c.Clarity] = cc + 1;
            proofHist.TryGetValue(c.ProofFacts.Count, out int pc); proofHist[c.ProofFacts.Count] = pc + 1;
        }
        Console.WriteLine($"CASE CLOSED Truth Engine (C#) - invariant sweep, {n} seeds");
        Console.WriteLine($"  guilty: {100.0 * guilty / n:F1}%   [target ~60%]");
        Console.WriteLine($"  clarity: " + string.Join(", ", clarity.OrderBy(k => k.Key).Select(k => $"{k.Key} {100.0 * k.Value / n:F1}%")));
        Console.WriteLine($"  witness-perp lies about crime slot: {100.0 * perpLies / n:F1}% of cases");
        Console.WriteLine($"  protector agendas: {100.0 * protectors / n:F1}% of cases");
        Console.WriteLine($"  avg rerolls/case: {(double)rerollsTotal / n:F2} | below proof floor after cap: {hardFails}  [target 0]");
        Console.WriteLine($"  corruption entries: {(double)ledgerTotal / n:F1}/case | reverted as uncounterable: {revertedCorruptions} total");
        Console.WriteLine($"  proof-fact histogram: " + string.Join(", ", proofHist.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")));
        return hardFails == 0 ? 0 : 2;
    }
    default:
        PrintHelp();
        return 1;
}

static void PrintHelp()
{
    Console.WriteLine("CaseClosed.Console - Truth Engine CLI");
    Console.WriteLine("  generate <seed> | kit <seed> <outDir> | scan <from> <to> | validate <n>");
}
