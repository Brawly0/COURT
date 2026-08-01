using System;
using System.Collections.Generic;
using System.Linq;
using CaseClosed.TruthEngine;
using Xunit;

namespace CaseClosed.Tests;

public class DeterminismTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalCase()
    {
        for (ulong s = 1; s <= 50; s++)
            Assert.Equal(CaseGenerator.Generate(s).Digest(), CaseGenerator.Generate(s).Digest());
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentCases()
    {
        var digests = new HashSet<string>();
        for (ulong s = 1; s <= 100; s++) digests.Add(CaseGenerator.Generate(s).Digest());
        Assert.True(digests.Count > 95, $"only {digests.Count} of 100 digests were unique");
    }
}

public class InvariantTests
{
    private static readonly List<CaseFile> Cases =
        Enumerable.Range(1, 400).Select(s => CaseGenerator.Generate((ulong)s)).ToList();

    [Fact]
    public void BaggageRule_AlwaysThreeFacts()
        => Assert.All(Cases, c => Assert.Equal(3, c.Baggage.Count));

    [Fact]
    public void Defendant_NeverGetsScriptedSelfPreservationLie()
        => Assert.All(Cases, c => Assert.DoesNotContain(c.Ledger,
            e => e.Kind == CorruptionKind.SelfPreservation && e.Witness == c.Defendant));

    [Fact]
    public void PerpLie_OnlyInInnocentDefendantCases()
        => Assert.All(Cases, c =>
        {
            if (c.PerpClaimedLocation >= 0) Assert.False(c.Guilty);
        });

    [Fact]
    public void StampContract_ClearFragmentsLieOnlyInFracturedCases()
        => Assert.All(Cases, c =>
        {
            var falseClear = c.Hand.Where(f => f.Stamp == Stamp.Clear && f.Fidelity == Fidelity.Corrupted).ToList();
            if (c.Clarity == Clarity.Fractured) Assert.NotEmpty(falseClear);
            else Assert.Empty(falseClear);
        });

    [Fact]
    public void FracturedCases_ShipTheToxicologyCause()
        => Assert.All(Cases.Where(c => c.Clarity == Clarity.Fractured),
            c => Assert.Contains(c.Evidence, e => e.Name.Contains("Toxicology")));

    [Fact]
    public void PooledSolvable_ProofChainMeetsFloor()
        => Assert.All(Cases, c => Assert.True(c.ProofFacts.Count >= World.MinProofFacts,
            $"seed {c.Seed}: only {c.ProofFacts.Count} proof facts"));

    [Fact]
    public void Detectability_EveryLedgerEntryCarriesACounter()
        => Assert.All(Cases, c => Assert.All(c.Ledger,
            e => Assert.False(string.IsNullOrWhiteSpace(e.CounterNote))));

    [Fact]
    public void LucidCases_ContainTheDecisiveFragment()
        => Assert.All(Cases.Where(c => c.Clarity == Clarity.Lucid),
            c => Assert.Contains(c.Hand, f => f.Text.Contains("taking " + c.CrimeObject)));

    [Fact]
    public void GuiltPrior_WithinBand()
    {
        double rate = Cases.Count(c => c.Guilty) / (double)Cases.Count;
        Assert.InRange(rate, 0.50, 0.70);
    }

    [Fact]
    public void ClarityMix_WithinBands()
    {
        double lucid = Cases.Count(c => c.Clarity == Clarity.Lucid) / (double)Cases.Count;
        double fractured = Cases.Count(c => c.Clarity == Clarity.Fractured) / (double)Cases.Count;
        Assert.InRange(lucid, 0.50, 0.70);
        Assert.InRange(fractured, 0.08, 0.22);
    }

    [Fact]
    public void OpeningStatements_NeverLeakTheProtectorTag()
        => Assert.All(Cases, c =>
        {
            foreach (var w in c.CastNames.Skip(1))
                Assert.All(KitWriter.OpeningStatement(c, w),
                    line => Assert.DoesNotContain("protect", line, StringComparison.OrdinalIgnoreCase));
        });

    [Fact]
    public void LyingPerp_ClaimsTheFalseLocationInTheirStatement()
        => Assert.All(Cases.Where(c => c.PerpClaimedLocation >= 0), c =>
        {
            var lines = KitWriter.OpeningStatement(c, c.Perpetrator);
            Assert.Contains(lines, l =>
                l.Contains($"I was in the {World.Locations[c.PerpClaimedLocation]} at {World.Slots[c.CrimeSlot]}"));
        });
}

public class JudgeMathTests
{
    [Fact]
    public void VerifiedOutweighsPhotoAndUnverified()
    {
        Assert.True(JudgeMath.AuthWeight(Authenticity.Verified) > JudgeMath.AuthWeight(Authenticity.Photo));
        Assert.Equal(JudgeMath.AuthWeight(Authenticity.Photo), JudgeMath.AuthWeight(Authenticity.Unverified));
        Assert.Equal(0.0, JudgeMath.AuthWeight(Authenticity.FakeDetected));
    }

    [Fact]
    public void DiminishingReturns_SecondSameCategoryCountsHalf()
    {
        var one = JudgeMath.ScoreSide(new[] { new Exhibit("prints", Authenticity.Verified, Relevance.Direct) });
        var two = JudgeMath.ScoreSide(new[]
        {
            new Exhibit("prints", Authenticity.Verified, Relevance.Direct),
            new Exhibit("prints", Authenticity.Verified, Relevance.Direct),
        });
        Assert.Equal(1.5 * one, two, 6);
    }

    [Fact]
    public void CustodyFloor_NeverBelowPoint4()
        => Assert.Equal(0.4, JudgeMath.CustodyWeight(10), 6);

    [Fact]
    public void MistrialBand_TriggersOnNearTies()
    {
        Assert.Equal(Verdict.Mistrial, JudgeMath.Decide(1.00, 0.78));
        Assert.Equal(Verdict.Guilty, JudgeMath.Decide(2.00, 1.00));
        Assert.Equal(Verdict.NotGuilty, JudgeMath.Decide(0.50, 1.00));
    }
}
