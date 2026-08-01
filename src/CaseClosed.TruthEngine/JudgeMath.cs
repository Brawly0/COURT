using System;
using System.Collections.Generic;

namespace CaseClosed.TruthEngine
{
    public enum Authenticity { Verified, Unverified, CertifiedCopy, Photo, FakeDetected }
    public enum Relevance { Direct, Corroborating, Circumstantial, Irrelevant }
    public enum Verdict { Guilty, NotGuilty, Mistrial }

    public sealed class Exhibit
    {
        public string Category;        // diminishing returns bucket (prints/cctv/docs/testimony/...)
        public Authenticity Auth;
        public Relevance Rel;
        public int CustodyGaps;
        public double WitnessCredibility; // testimony only; <= 0 means not testimony

        public Exhibit(string category, Authenticity auth, Relevance rel, int custodyGaps = 0, double witnessCredibility = -1)
        {
            Category = category; Auth = auth; Rel = rel;
            CustodyGaps = custodyGaps; WitnessCredibility = witnessCredibility;
        }
    }

    /// <summary>
    /// The deterministic judge (GDD 05). S = A x C x R, category diminishing
    /// returns, reasonable-doubt multiplier M, 8% mistrial band. No LLM, no
    /// randomness, no rhetoric — the record decides. All constants TUNE.
    /// </summary>
    public static class JudgeMath
    {
        public const double DefaultM = 1.25;         // reasonable-doubt multiplier (1.10-1.50 by judge)
        public const double MistrialBand = 0.08;     // widened from 0.05 per Phase 0 sim
        public static readonly double[] Diminish = { 1.0, 0.5, 0.25, 0.25, 0.25, 0.25 };

        public static double AuthWeight(Authenticity a) => a switch
        {
            Authenticity.Verified => 1.0,
            Authenticity.Unverified => 0.7,
            Authenticity.CertifiedCopy => 0.9,
            Authenticity.Photo => 0.7,
            _ => 0.0, // FakeDetected
        };

        public static double RelWeight(Relevance r) => r switch
        {
            Relevance.Direct => 1.0,
            Relevance.Corroborating => 0.6,
            Relevance.Circumstantial => 0.3,
            _ => 0.0, // Irrelevant
        };

        public static double CustodyWeight(int gaps) => Math.Max(0.4, 1.0 - 0.2 * gaps);

        public static double ScoreExhibit(Exhibit e)
        {
            double a = e.WitnessCredibility > 0 ? e.WitnessCredibility : AuthWeight(e.Auth);
            return a * CustodyWeight(e.CustodyGaps) * RelWeight(e.Rel);
        }

        /// <summary>Sum a side's exhibits with per-category diminishing returns (anti-spam, GDD 05).</summary>
        public static double ScoreSide(IEnumerable<Exhibit> exhibits, double multiplier = 1.0)
        {
            var perCategory = new Dictionary<string, int>();
            var scored = new List<(double s, string cat)>();
            foreach (var e in exhibits) scored.Add((ScoreExhibit(e), e.Category));
            scored.Sort((x, y) => y.s.CompareTo(x.s)); // strongest first per GDD
            double total = 0;
            foreach (var (s, cat) in scored)
            {
                perCategory.TryGetValue(cat, out int n);
                total += s * Diminish[Math.Min(n, Diminish.Length - 1)];
                perCategory[cat] = n + 1;
            }
            return total * multiplier;
        }

        public static Verdict Decide(double guiltScore, double doubtScore, double m = DefaultM, double moodShift = 1.0)
        {
            double threshold = doubtScore * m * moodShift;
            double hi = Math.Max(Math.Max(guiltScore, threshold), 0.001);
            if (Math.Abs(guiltScore - threshold) <= MistrialBand * hi) return Verdict.Mistrial;
            return guiltScore >= threshold ? Verdict.Guilty : Verdict.NotGuilty;
        }
    }
}
