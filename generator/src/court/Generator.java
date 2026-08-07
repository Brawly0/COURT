package court;

import court.Model.*;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Random;
import java.util.Set;

/**
 * The fact-graph generator. Master GDD Part III, steps 1-7.
 * Step 8 (the solvability solver) is deliberately absent until we have read
 * enough real cases to define what "an inference step" actually means.
 *
 * Everything is driven by one seeded Random, so a seed reproduces a case exactly.
 */
public final class Generator {

    /** P(an innocent defendant's prints are on the object anyway). The sting dial. */
    private static final double DEFENDANT_PRINT_CHANCE = 0.55;

    /** Same, when the timeline never put him in the crime room -- he handled it earlier. */
    private static final double DEFENDANT_PRINT_CHANCE_REMOTE = 0.45;

    /** P(whoever did it left a usable print). Below 1.0 on purpose -- see derivePrints. */
    private static final double PERPETRATOR_PRINT_CHANCE = 0.7;

    private final Random rnd;
    private final Config cfg;
    private final long seed;

    public Generator(long seed, Config cfg) {
        this.seed = seed;
        this.cfg = cfg;
        this.rnd = new Random(seed);
    }

    public CourtCase generate() {
        CourtCase c = new CourtCase();
        c.seed = seed;
        c.cfg = cfg;
        c.venueDef = World.VENUES[rnd.nextInt(World.VENUES.length)];
        c.venue = c.venueDef.name();
        c.locations = c.venueDef.rooms();

        buildCast(c);
        buildOccupancy(c);
        placeCrime(c);
        derivePrints(c);
        placeCameras(c);
        deriveObservations(c);
        corruptMemories(c);
        assignAgendas(c);
        buildDefendantBaggage(c);
        buildFacts(c);
        dealFacts(c);
        return c;
    }

    // ---------------------------------------------------------------- step 0

    private void buildCast(CourtCase c) {
        List<String> names = new ArrayList<>(List.of(World.NAMES));
        Collections.shuffle(names, rnd);

        int n = cfg.characters;
        // One descriptor per pair, so PERSON_SWAP always has a plausible target.
        int poolSize = Math.max(2, (n + 1) / 2);
        List<String> descs = new ArrayList<>(List.of(World.DESCRIPTORS).subList(0, poolSize));
        Collections.shuffle(descs, rnd);

        for (int i = 0; i < n; i++) {
            c.cast.add(new Person(i, names.get(i), descs.get(i % poolSize), i == 0));
        }
    }

    // ---------------------------------------------------------------- step 1

    /** Constrained random walk over the adjacency map. This is ground truth. */
    private void buildOccupancy(CourtCase c) {
        int n = cfg.characters;
        int t = cfg.slots;
        int L = World.LOCATION_COUNT;
        int[][] occ = new int[n][t];

        for (int p = 0; p < n; p++) {
            occ[p][0] = rnd.nextInt(L);
            for (int s = 1; s < t; s++) {
                if (rnd.nextDouble() < cfg.stayProbability) {
                    occ[p][s] = occ[p][s - 1];
                } else {
                    int[] adj = World.ADJ[occ[p][s - 1]];
                    occ[p][s] = adj[rnd.nextInt(adj.length)];
                }
            }
        }
        c.occupancy = occ;
    }

    // ---------------------------------------------------------------- step 2

    /**
     * The crime is not placed at random — it is placed where it makes a case.
     *
     * Reading the first fifty outputs taught us that an unconstrained (t*, l*) very
     * often produces a room nobody visited, which means: one set of prints (case
     * solved instantly), and an innocent defendant with a perfect alibi and nothing
     * to answer for (no case at all). Both are dead sessions.
     *
     * So we score every legal (t*, l*) pair and take the best. The requirements are:
     *   - somebody other than the perpetrator has been through that room (print pool)
     *   - the defendant is entangled: in the room, or in it earlier, or next door
     */
    private void placeCrime(CourtCase c) {
        int n = cfg.characters;
        int T = cfg.slots;
        int L = World.LOCATION_COUNT;
        int[][] occ = c.occupancy;

        boolean guilty = rnd.nextDouble() < cfg.guiltProbability;

        List<int[]> best = new ArrayList<>();   // {slot, location}
        int bestScore = Integer.MIN_VALUE;

        // Never the first slot (no history) and never the last (no aftermath).
        int lastUsable = Math.max(1, T - 2);
        for (int t = 1; t <= lastUsable; t++) {
            for (int l = 0; l < L; l++) {
                boolean defendantHere = occ[0][t] == l;
                if (guilty != defendantHere) continue;

                List<Integer> presentOthers = new ArrayList<>();
                for (int p = 1; p < n; p++) if (occ[p][t] == l) presentOthers.add(p);
                if (!guilty && presentOthers.isEmpty()) continue;   // somebody has to have done it

                Set<Integer> visitors = new LinkedHashSet<>();
                boolean defendantEarlier = false;
                for (int p = 0; p < n; p++) {
                    for (int s = 0; s < T; s++) {
                        if (occ[p][s] == l) {
                            visitors.add(p);
                            if (p == 0 && s < t) defendantEarlier = true;
                        }
                    }
                }

                // Bystanders = people in the room who are neither the defendant nor
                // the perpetrator. This MUST be scored identically for guilty and
                // innocent cases: if innocent cases systematically have no eyewitness,
                // "nobody saw it" becomes a proof of innocence and the game is solved.
                int bystanders = guilty ? presentOthers.size()
                                        : Math.max(0, presentOthers.size() - 1);

                int score = 0;
                if (visitors.size() >= 2) score += 3;      // a real print pool
                if (visitors.size() >= 3) score += 1;
                if (defendantEarlier) score += 3;          // he was in that room. explain that.
                if (!guilty && World.adjacent(occ[0][t], l)) score += 2;   // close enough to accuse

                // Weighted above every other term, deliberately. Anything that can
                // outbid "was somebody watching" will pull one guilt branch toward
                // empty rooms and turn the empty room into a verdict.
                score += Math.min(bystanders, 2) * 5;

                if (score > bestScore) { bestScore = score; best.clear(); }
                if (score == bestScore) best.add(new int[]{t, l});
            }
        }

        // Extremely rare: the guilt roll is impossible for this walk. Flip it rather
        // than regenerate, so the seed still maps to one deterministic case.
        if (best.isEmpty()) {
            guilty = !guilty;
            for (int t = 1; t <= lastUsable; t++) {
                for (int l = 0; l < L; l++) {
                    boolean defendantHere = occ[0][t] == l;
                    if (guilty != defendantHere) continue;
                    boolean others = false;
                    for (int p = 1; p < n; p++) if (occ[p][t] == l) others = true;
                    if (!guilty && !others) continue;
                    best.add(new int[]{t, l});
                }
            }
        }

        int[] chosen = best.get(rnd.nextInt(best.size()));
        int tStar = chosen[0];
        int lStar = chosen[1];

        int perp;
        if (guilty) {
            perp = 0;
        } else {
            List<Integer> present = new ArrayList<>();
            for (int p = 1; p < n; p++) if (occ[p][tStar] == lStar) present.add(p);
            perp = present.get(rnd.nextInt(present.size()));
        }

        String[][] pool = c.venueDef.crimes();
        String[] crime = pool[rnd.nextInt(pool.length)];
        int[] adj = World.ADJ[lStar];
        int movedTo = adj[rnd.nextInt(adj.length)];

        c.crime = new Crime(tStar, lStar, perp, perp == 0, crime[0], crime[1], movedTo);
    }

    // ---------------------------------------------------------------- step 3

    /**
     * Prints prove contact, not time. The perpetrator plus one or two people who
     * handled the thing earlier for entirely innocent reasons.
     */
    private void derivePrints(CourtCase c) {
        Set<Integer> prints = new LinkedHashSet<>();

        // The perpetrator does NOT always leave a usable print. If they did, then
        // "the defendant's prints are missing" would be a proof of innocence --
        // certain, unarguable, and it would end the case in one sentence.
        if (rnd.nextDouble() < PERPETRATOR_PRINT_CHANCE) prints.add(c.crime.perpetrator());

        List<Integer> earlierHandlers = new ArrayList<>();
        for (int p = 0; p < cfg.characters; p++) {
            for (int t = 0; t < c.crime.slot(); t++) {
                if (c.occupancy[p][t] == c.crime.location()) { earlierHandlers.add(p); break; }
            }
        }
        Collections.shuffle(earlierHandlers, rnd);

        // The defendant's inclusion is an explicit coin, not a side effect of ordering.
        // Because the crime is placed in a room he passed through, he is nearly always
        // *eligible* -- and if eligibility decided it, "his prints are on it" would be
        // true in ~90% of cases and would therefore mean nothing at trial.
        boolean wasInRoomEarlier = earlierHandlers.contains(0);
        earlierHandlers.remove(Integer.valueOf(0));
        if (c.crime.perpetrator() != 0) {
            // If the matrix never put him in that room, he can still have handled the
            // thing before the evening began -- carrying it in, moving it off a table.
            // Without this path his prints depend on his walk, and the print rate
            // ends up carrying far more signal than a fingerprint deserves.
            double chance = wasInRoomEarlier ? DEFENDANT_PRINT_CHANCE : DEFENDANT_PRINT_CHANCE_REMOTE;
            if (rnd.nextDouble() < chance) {
                prints.add(0);
                c.defendantHandledBeforeTimeline = !wasInRoomEarlier;
            }
        }

        for (int p : earlierHandlers) {
            if (prints.size() >= 3) break;
            prints.add(p);
        }
        c.printsOnObject = new ArrayList<>(prints);
    }

    private void placeCameras(CourtCase c) {
        int L = World.LOCATION_COUNT;
        List<Integer> locs = new ArrayList<>();
        for (int i = 0; i < L; i++) locs.add(i);
        Collections.shuffle(locs, rnd);

        c.cameraCovered = new boolean[L];
        for (int i = 0; i < Math.min(cfg.cameraLocations, L); i++) {
            c.cameraCovered[locs.get(i)] = true;
        }
    }

    // ---------------------------------------------------------------- step 5

    /** Obs(X) = what X could physically have seen or heard. Nothing more. */
    private void deriveObservations(CourtCase c) {
        int n = cfg.characters;
        int T = cfg.slots;
        int tStar = c.crime.slot();
        int lStar = c.crime.location();

        for (int x = 0; x < n; x++) {
            List<Observation> obs = new ArrayList<>();

            for (int t = 0; t < T; t++) {
                for (int y = 0; y < n; y++) {
                    if (y == x) continue;
                    if (c.occupancy[x][t] == c.occupancy[y][t]) {
                        obs.add(new Observation(x, ObsKind.SAW_PERSON, y, c.occupancy[x][t], t));
                    }
                }
            }

            // Next door at the moment of the crime.
            if (c.occupancy[x][tStar] != lStar && World.adjacent(c.occupancy[x][tStar], lStar)) {
                obs.add(new Observation(x, ObsKind.HEARD_NOISE, -1, lStar, tStar));
            }

            // In the room at the moment of the crime, and not the one who did it.
            if (c.occupancy[x][tStar] == lStar && x != c.crime.perpetrator()) {
                obs.add(new Observation(x, ObsKind.SAW_LEAVE, c.crime.perpetrator(), lStar, tStar));
            }

            // Keep the card readable: prefer what happened near the crime.
            obs.sort(Comparator.comparingInt(o ->
                    (o.kind == ObsKind.SAW_PERSON ? 10 : 0) + Math.abs(o.slot - tStar)));
            if (obs.size() > cfg.maxObservations) obs = new ArrayList<>(obs.subList(0, cfg.maxObservations));
            obs.sort(Comparator.comparingInt(o -> o.slot));

            c.observations.put(x, obs);
        }
    }

    // ---------------------------------------------------------------- step 6

    /**
     * The engine of contradiction. Testimony conflicts without anybody lying.
     * The witness is never told which of their memories is false.
     */
    private void corruptMemories(CourtCase c) {
        for (int x = 0; x < cfg.characters; x++) {
            List<Observation> obs = c.observations.get(x);
            if (obs.isEmpty()) continue;

            List<Observation> pool = new ArrayList<>(obs);
            Collections.shuffle(pool, rnd);

            int budget = Math.min(cfg.corruptionsPerWitness, pool.size());
            for (int i = 0; i < budget; i++) {
                corrupt(c, pool.get(i));
            }

            // A time shift can land a memory exactly on top of a real one. Drop the
            // duplicate -- a card that says the same line twice just looks like a bug.
            List<Observation> deduped = new ArrayList<>();
            Set<String> seen = new java.util.HashSet<>();
            for (Observation o : obs) {
                String key = o.kind + "|" + o.other + "|" + o.location + "|" + o.slot;
                if (seen.add(key)) deduped.add(o);
            }
            deduped.sort(Comparator.comparingInt(o -> o.slot));
            c.observations.put(x, deduped);
        }
    }

    private void corrupt(CourtCase c, Observation o) {
        if (o.kind == ObsKind.HEARD_NOISE) {
            // Promote it: they heard a door, they remember a face.
            List<Integer> plausible = new ArrayList<>();
            for (int p = 0; p < cfg.characters; p++) {
                if (p != o.owner) plausible.add(p);
            }
            int guess = plausible.get(rnd.nextInt(plausible.size()));
            o.corruption = Corruption.PROMOTION;
            o.truthNote = "only heard a noise from " + c.loc(o.location)
                    + "; remembers it as seeing " + c.person(guess).name();
            o.kind = ObsKind.SAW_LEAVE;
            o.other = guess;
            return;
        }

        boolean canSwap = o.other >= 0;
        int roll = rnd.nextInt(canSwap ? 2 : 1);

        if (roll == 0) {
            int delta = rnd.nextBoolean() ? 1 : -1;
            int shifted = o.slot + delta;
            if (shifted < 0 || shifted >= cfg.slots) shifted = o.slot - delta;
            if (shifted == o.slot || shifted < 0 || shifted >= cfg.slots) {
                if (canSwap) { swapPerson(c, o); return; }
                return;
            }
            o.corruption = Corruption.TIME_SHIFT;
            o.truthNote = "really happened at " + World.SLOT_LABELS[o.slot]
                    + ", they will say " + World.SLOT_LABELS[shifted];
            o.slot = shifted;
        } else {
            swapPerson(c, o);
        }
    }

    private void swapPerson(CourtCase c, Observation o) {
        String desc = c.person(o.other).descriptor();
        List<Integer> lookalikes = new ArrayList<>();
        for (Person p : c.cast) {
            if (p.id() != o.other && p.id() != o.owner && p.descriptor().equals(desc)) {
                lookalikes.add(p.id());
            }
        }
        if (lookalikes.isEmpty()) return;

        int replacement = lookalikes.get(rnd.nextInt(lookalikes.size()));
        o.corruption = Corruption.PERSON_SWAP;
        o.truthNote = "it was actually " + c.person(o.other).name()
                + "; same " + desc + ", they will name " + c.person(replacement).name();
        o.other = replacement;
    }

    // ---------------------------------------------------------------- step 7

    /** The rare, actual liars. Rare enough that accusing everyone is a losing strategy. */
    private void assignAgendas(CourtCase c) {
        if (cfg.agendas <= 0) return;

        List<Integer> candidates = new ArrayList<>();
        for (int p = 1; p < cfg.characters; p++) candidates.add(p);
        Collections.shuffle(candidates, rnd);

        int count = Math.min(cfg.agendas, candidates.size());
        for (int i = 0; i < count; i++) {
            int owner = candidates.get(i);
            c.agendas.add(makeAgenda(c, owner));
        }
    }

    private Agenda makeAgenda(CourtCase c, int owner) {
        int type = rnd.nextInt(3);
        int tStar = c.crime.slot();
        String lStarName = c.loc(c.crime.location());

        if (type == 0) {
            String slot = World.SLOT_LABELS[rnd.nextInt(cfg.slots)];
            String tpl = World.AGENDA_AFFAIR[rnd.nextInt(World.AGENDA_AFFAIR.length)];
            return new Agenda(owner, "AFFAIR", String.format(tpl, slot));
        }
        if (type == 1) {
            List<Integer> others = new ArrayList<>();
            for (int p = 0; p < cfg.characters; p++) if (p != owner) others.add(p);
            String who = c.person(others.get(rnd.nextInt(others.size()))).name();
            String tpl = World.AGENDA_PROTECT[rnd.nextInt(World.AGENDA_PROTECT.length)];
            return new Agenda(owner, "PROTECT", String.format(tpl, who, lStarName));
        }
        String tpl = World.AGENDA_GRUDGE[rnd.nextInt(World.AGENDA_GRUDGE.length)];
        return new Agenda(owner, "GRUDGE", String.format(tpl, lStarName, World.SLOT_LABELS[tStar]));
    }

    // ---------------------------------------------------------------- step 4

    /**
     * Non-negotiable per the GDD: always 2-3 benign reasons the defendant looks
     * guilty, regardless of actual guilt. Without this, "looks guilty" and
     * "is guilty" collapse into one signal and the game stops working.
     */
    private void buildDefendantBaggage(CourtCase c) {
        List<String> pool = new ArrayList<>();
        int tStar = c.crime.slot();
        int lStar = c.crime.location();
        int T = cfg.slots;

        // Every entry below is DERIVED from the occupancy matrix. Nothing here may
        // contradict ground truth -- witnesses will be testifying against the same
        // timeline, and a card that invents facts gets caught in ninety seconds.

        if (c.printsOnObject.contains(0)) {
            pool.add(c.defendantHandledBeforeTimeline
                    ? "Your prints are on " + c.crime.objectName()
                      + ". You helped carry it in before the evening started. Nobody remembers that but you."
                    : "Your prints are on " + c.crime.objectName()
                      + ". You handled it earlier, for a reason that sounds bad out loud.");
        }

        int lastVisit = -1;
        for (int t = 0; t < tStar; t++) if (c.occupancy[0][t] == lStar) lastVisit = t;
        if (lastVisit >= 0) {
            pool.add("You were in " + c.loc(lStar) + " at " + World.SLOT_LABELS[lastVisit]
                    + ". People saw you there.");
        }

        // A slot where nobody could corroborate him is the one worth lying about.
        List<Integer> aloneSlots = new ArrayList<>();
        for (int t = 0; t < T; t++) {
            boolean alone = true;
            for (int p = 1; p < cfg.characters; p++) {
                if (c.occupancy[p][t] == c.occupancy[0][t]) { alone = false; break; }
            }
            if (alone) aloneSlots.add(t);
        }
        if (!aloneSlots.isEmpty()) {
            int t = aloneSlots.get(rnd.nextInt(aloneSlots.size()));
            pool.add("Nobody can put you anywhere at " + World.SLOT_LABELS[t]
                    + ", and you will not say where you were: "
                    + World.EMBARRASSING[rnd.nextInt(World.EMBARRASSING.length)] + ".");
        }

        // Only claim he hurried out of somewhere he actually left.
        List<Integer> moves = new ArrayList<>();
        for (int t = 1; t < T; t++) if (c.occupancy[0][t] != c.occupancy[0][t - 1]) moves.add(t);
        if (!moves.isEmpty()) {
            moves.sort(Comparator.comparingInt(t -> Math.abs(t - tStar)));
            int t = moves.get(0);
            pool.add("You were seen leaving " + c.loc(c.occupancy[0][t - 1])
                    + " in a hurry at " + World.SLOT_LABELS[t] + ".");
        }

        if (World.adjacent(c.occupancy[0][tStar], lStar)) {
            pool.add("You were next door to " + c.loc(lStar)
                    + " at " + World.SLOT_LABELS[tStar] + " and heard nothing. Say that out loud and hear how it sounds.");
        }

        pool.add("Motive: " + World.MOTIVES[rnd.nextInt(World.MOTIVES.length)] + ".");

        Collections.shuffle(pool, rnd);
        int want = Math.min(cfg.incriminatingFacts, pool.size());
        for (int i = 0; i < want; i++) c.defendantBaggage.add(pool.get(i));
    }

    // ------------------------------------------------------- forensic facts

    private void buildFacts(CourtCase c) {
        int tStar = c.crime.slot();
        int lStar = c.crime.location();
        boolean guilty = c.crime.defendantGuilty();

        for (int p : c.printsOnObject) {
            String text = c.person(p).name() + "'s prints are on " + c.crime.objectName() + ".";
            Favors f;
            if (p == 0) f = guilty ? Favors.PROSECUTION : Favors.MISLEADING;
            else f = Favors.DEFENSE;
            c.allFacts.add(new Fact(text, f, p));
        }

        c.allFacts.add(new Fact(
                c.crime.objectName() + " " + c.crime.verb() + " " + c.loc(lStar)
                        + " and ended up in " + c.loc(c.crime.objectMovedTo()) + ".",
                Favors.NEUTRAL, -1));

        for (int p = 0; p < cfg.characters; p++) {
            for (int t = 0; t < cfg.slots; t++) {
                int loc = c.occupancy[p][t];
                if (!c.cameraCovered[loc]) continue;
                if (Math.abs(t - tStar) > 1) continue;

                String text = "Camera: " + c.person(p).name() + " is on tape in "
                        + c.loc(loc) + " at " + World.SLOT_LABELS[t] + ".";
                Favors f = Favors.NEUTRAL;
                if (p == 0 && loc == lStar) f = guilty ? Favors.PROSECUTION : Favors.MISLEADING;
                else if (p == 0) f = Favors.DEFENSE;                 // alibi
                else if (loc == lStar && t == tStar) f = Favors.DEFENSE; // someone else was there
                c.allFacts.add(new Fact(text, f, p));
            }
        }

        // Guarantee the prosecution has something to stand up with. If the defendant
        // is entangled with the crime room at all, that entanglement becomes a fact --
        // damning if he did it, a trap for everyone if he did not.
        int lastVisit = -1;
        for (int t = 0; t < tStar; t++) if (c.occupancy[0][t] == lStar) lastVisit = t;
        if (lastVisit >= 0) {
            c.allFacts.add(new Fact(
                    "The hall's door log puts " + c.defendant().name() + " in " + c.loc(lStar)
                            + " at " + World.SLOT_LABELS[lastVisit] + ".",
                    guilty ? Favors.PROSECUTION : Favors.MISLEADING, 0));
        }
        if (World.adjacent(c.occupancy[0][tStar], lStar)) {
            c.allFacts.add(new Fact(
                    c.defendant().name() + " was in " + c.loc(c.occupancy[0][tStar])
                            + " at " + World.SLOT_LABELS[tStar] + " -- one door from "
                            + c.loc(lStar) + ".",
                    guilty ? Favors.PROSECUTION : Favors.MISLEADING, 0));
        }

        String decoy = World.DECOY_OBJECTS[rnd.nextInt(World.DECOY_OBJECTS.length)];
        int who = 1 + rnd.nextInt(Math.max(1, cfg.characters - 1));
        c.allFacts.add(new Fact(
                "Unidentified prints on " + decoy + " near " + c.loc(lStar)
                        + ". Later matched to " + c.person(who).name() + ", who has no connection to any of this.",
                Favors.NEUTRAL, who));
    }

    /**
     * Prosecution: at least one fact pointing at the defendant, at most one misleading.
     * Defense: at least one exculpatory.
     */
    private void dealFacts(CourtCase c) {
        List<Fact> pool = new ArrayList<>(c.allFacts);
        Collections.shuffle(pool, rnd);

        Fact accusing = pick(pool, f -> f.favors() == Favors.PROSECUTION || f.favors() == Favors.MISLEADING);
        if (accusing != null) c.prosecutionFacts.add(accusing);

        while (c.prosecutionFacts.size() < cfg.prosecutionFacts && !pool.isEmpty()) {
            long misleading = c.prosecutionFacts.stream().filter(f -> f.favors() == Favors.MISLEADING).count();
            Fact next = (misleading >= 1)
                    ? pick(pool, f -> f.favors() != Favors.MISLEADING)
                    : pool.remove(0);
            if (next == null) next = pool.remove(0);
            c.prosecutionFacts.add(next);
        }

        Fact exculpatory = pick(pool, f -> f.favors() == Favors.DEFENSE);
        if (exculpatory != null) c.defenseFacts.add(exculpatory);

        while (c.defenseFacts.size() < cfg.defenseFacts && !pool.isEmpty()) {
            c.defenseFacts.add(pool.remove(0));
        }
    }

    private Fact pick(List<Fact> pool, java.util.function.Predicate<Fact> test) {
        for (int i = 0; i < pool.size(); i++) {
            if (test.test(pool.get(i))) return pool.remove(i);
        }
        return null;
    }
}
