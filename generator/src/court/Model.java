package court;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Data only. Everything here is structured rather than pre-rendered strings,
 * so the solvability solver can be written against it later without a rewrite.
 */
public final class Model {

    public record Person(int id, String name, String descriptor, boolean defendant) {}

    public record Crime(
            int slot,
            int location,
            int perpetrator,
            boolean defendantGuilty,
            String objectName,
            String verb,
            int objectMovedTo) {}

    public enum ObsKind {
        /** X and Y were in the same room at T. */
        SAW_PERSON,
        /** X was next door to the crime room at T and heard something. */
        HEARD_NOISE,
        /** X was in the crime room at T and saw someone leaving. */
        SAW_LEAVE
    }

    public enum Corruption {
        NONE,
        /** The memory is real but filed under the wrong half-hour. */
        TIME_SHIFT,
        /** Right jacket, wrong person. */
        PERSON_SWAP,
        /** They heard a door and remember it as having seen someone. */
        PROMOTION
    }

    /** One line on a witness card. The witness believes all of them equally. */
    public static final class Observation {
        public int owner;
        public ObsKind kind;
        public int other = -1;     // person id, or -1
        public int location;
        public int slot;
        public Corruption corruption = Corruption.NONE;
        /** Ground truth only. Never printed on a card. */
        public String truthNote = "";

        public Observation(int owner, ObsKind kind, int other, int location, int slot) {
            this.owner = owner;
            this.kind = kind;
            this.other = other;
            this.location = location;
            this.slot = slot;
        }

        public boolean corrupt() { return corruption != Corruption.NONE; }
    }

    public enum Favors {
        /** Points at the defendant, and the defendant did it. */
        PROSECUTION,
        /** Points away from the defendant. */
        DEFENSE,
        /** True, unhelpful. */
        NEUTRAL,
        /** Points at the defendant, and the defendant did not do it. */
        MISLEADING
    }

    public record Fact(String text, Favors favors, int pointsAt) {}

    public record Agenda(int owner, String label, String instruction) {}

    public static final class CourtCase {
        public long seed;
        public Config cfg;
        public World.Venue venueDef;
        public String venue;
        /** Room names for this case's venue. Indices match World.ADJ. */
        public String[] locations;

        public List<Person> cast = new ArrayList<>();
        /** occupancy[person][slot] = location. Ground truth. No player ever sees this. */
        public int[][] occupancy;
        public Crime crime;

        public List<Integer> printsOnObject = new ArrayList<>();
        /** True when the defendant's prints come from before the timeline starts. */
        public boolean defendantHandledBeforeTimeline = false;
        public boolean[] cameraCovered;

        public Map<Integer, List<Observation>> observations = new LinkedHashMap<>();
        public List<Agenda> agendas = new ArrayList<>();
        public List<String> defendantBaggage = new ArrayList<>();

        public List<Fact> allFacts = new ArrayList<>();
        public List<Fact> prosecutionFacts = new ArrayList<>();
        public List<Fact> defenseFacts = new ArrayList<>();

        public Person defendant() { return cast.get(0); }
        public Person person(int id) { return cast.get(id); }
        public String slotLabel(int t) { return World.SLOT_LABELS[t]; }
        public String loc(int l) { return locations[l]; }
    }

    private Model() {}
}
