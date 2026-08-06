package court;

import court.Model.CourtCase;

/**
 * Gate 1 tool.
 *
 *   java -cp bin court.Main                          one case, default settings
 *   java -cp bin court.Main --seed 42                reproduce exactly that case
 *   java -cp bin court.Main --case 4                 use the escalation preset for case 4
 *   java -cp bin court.Main --count 50 --brief       scan fifty cases
 *   java -cp bin court.Main --count 5                five full cases
 *   java -cp bin court.Main --stats 2000             distribution sanity check
 */
public final class Main {

    public static void main(String[] args) {
        long seed = System.currentTimeMillis() % 100000;
        boolean seedGiven = false;
        int count = 1;
        boolean brief = false;
        int statsRuns = 0;
        Config cfg = null;

        for (int i = 0; i < args.length; i++) {
            switch (args[i]) {
                case "--seed"  -> { seed = Long.parseLong(args[++i]); seedGiven = true; }
                case "--count" -> count = Integer.parseInt(args[++i]);
                case "--case"  -> cfg = Config.forCaseNumber(Integer.parseInt(args[++i]));
                case "--brief" -> brief = true;
                case "--stats" -> statsRuns = Integer.parseInt(args[++i]);
                case "--help"  -> { help(); return; }
                default -> { System.out.println("unknown argument: " + args[i]); help(); return; }
            }
        }
        if (cfg == null) cfg = new Config();

        if (statsRuns > 0) { stats(seed, cfg, statsRuns); return; }

        Printer printer = new Printer();
        for (int i = 0; i < count; i++) {
            long s = seedGiven ? seed + i : seed + i * 7919L;
            CourtCase c = new Generator(s, cfg).generate();
            System.out.print(brief ? printer.brief(c) : printer.full(c));
        }
    }

    /**
     * Distribution check AND leak detection.
     *
     * The second one matters more. Any surface feature that correlates with guilt is
     * a tell -- players will find it, and once they do the case is decided before
     * anybody speaks. We measure P(guilty | feature) against the base rate and shout
     * when it drifts. A leak is invisible in one case and fatal across thirty.
     */
    private static void stats(long seed, Config cfg, int runs) {
        int guilty = 0, corruptTotal = 0;
        int noEye = 0, noEyeGuilty = 0;
        int eye = 0, eyeGuilty = 0;
        int printed = 0, printedGuilty = 0;
        int unprinted = 0, unprintedGuilty = 0;
        int emptyPrints = 0;

        for (int i = 0; i < runs; i++) {
            CourtCase c = new Generator(seed + i * 7919L, cfg).generate();
            boolean g = c.crime.defendantGuilty();
            if (g) guilty++;

            boolean sawIt = false;
            for (int p = 0; p < cfg.characters; p++) {
                if (p != c.crime.perpetrator() && p != 0
                        && c.occupancy[p][c.crime.slot()] == c.crime.location()) sawIt = true;
            }
            if (sawIt) { eye++; if (g) eyeGuilty++; } else { noEye++; if (g) noEyeGuilty++; }

            if (c.printsOnObject.contains(0)) { printed++; if (g) printedGuilty++; }
            else { unprinted++; if (g) unprintedGuilty++; }
            if (c.printsOnObject.isEmpty()) emptyPrints++;

            for (var obs : c.observations.values()) {
                for (var o : obs) if (o.corrupt()) corruptTotal++;
            }
        }

        double base = 100.0 * guilty / runs;
        System.out.printf("runs                                  %d%n", runs);
        System.out.printf("defendant guilty (base rate)          %.1f%%   (target %.0f%%)%n",
                base, 100 * cfg.guiltProbability);
        System.out.printf("false memories per case               %.1f%n", (double) corruptTotal / runs);
        System.out.printf("no usable prints recovered at all     %.1f%%%n", 100.0 * emptyPrints / runs);
        System.out.println();
        System.out.println("LEAK CHECK -- P(guilty | feature) vs base rate");
        leak("an eyewitness was in the room", eyeGuilty, eye, base, runs);
        leak("nobody was in the room", noEyeGuilty, noEye, base, runs);
        leak("defendant's prints on the object", printedGuilty, printed, base, runs);
        leak("defendant's prints absent", unprintedGuilty, unprinted, base, runs);
    }

    private static void leak(String label, int guiltyCount, int total, double base, int runs) {
        if (total == 0) { System.out.printf("  %-36s  never happens%n", label); return; }
        double p = 100.0 * guiltyCount / total;
        double drift = p - base;
        String flag = Math.abs(drift) > 25 ? "   <== LEAK"
                    : Math.abs(drift) > 12 ? "   <-- watch" : "";
        System.out.printf("  %-36s %5.1f%%   drift %+5.1f   seen in %4.1f%% of cases%s%n",
                label, p, drift, 100.0 * total / runs, flag);
    }

    private static void help() {
        System.out.println("""
            COURT case generator

              --seed N      reproduce a specific case
              --case 1..5   escalation preset from the GDD
              --count N     generate N cases
              --brief       one-block summary per case, for scanning
              --stats N     run N cases and report distributions only
            """);
    }

    private Main() {}
}
