package court;

/**
 * Every tunable number in the generator lives here.
 * This is the file you edit during Gate 1 tuning.
 */
public final class Config {

    /** Cast size, including the defendant. */
    public int characters = 6;

    /** Number of time slots in the night. */
    public int slots = 6;

    /** How many of each witness's memories are false (they are not told which). */
    public int corruptionsPerWitness = 2;

    /** How many characters get a private reason to deliberately lie. */
    public int agendas = 1;

    /** P(the defendant actually did it). Master GDD says ~0.55. */
    public double guiltProbability = 0.55;

    /** P(a character stays put between two slots) during the random walk. */
    public double stayProbability = 0.45;

    /** Forensic facts dealt to each side up front (v0.1 distribution — right for paper play). */
    public int prosecutionFacts = 3;
    public int defenseFacts = 2;

    /** Benign-but-incriminating facts about the defendant. Generated regardless of guilt. */
    public int incriminatingFacts = 3;

    /** How many of the 5 locations have a working camera. */
    public int cameraLocations = 2;

    /** Cap on observations per witness card, so cards stay readable. */
    public int maxObservations = 6;

    /** Master GDD 3.4 escalation table. Case 1 is the tutorial, case 5 is the finale. */
    public static Config forCaseNumber(int n) {
        Config c = new Config();
        switch (n) {
            case 1 -> { c.characters = 4; c.slots = 4; c.corruptionsPerWitness = 1; c.agendas = 0; }
            case 2 -> { c.characters = 5; c.slots = 5; c.corruptionsPerWitness = 2; c.agendas = 1; }
            case 3 -> { c.characters = 6; c.slots = 5; c.corruptionsPerWitness = 3; c.agendas = 1; }
            case 4 -> { c.characters = 6; c.slots = 6; c.corruptionsPerWitness = 3; c.agendas = 2; }
            case 5 -> { c.characters = 7; c.slots = 6; c.corruptionsPerWitness = 4; c.agendas = 2; }
            default -> throw new IllegalArgumentException("case number must be 1..5");
        }
        return c;
    }

    public Config copy() {
        Config c = new Config();
        c.characters = characters;
        c.slots = slots;
        c.corruptionsPerWitness = corruptionsPerWitness;
        c.agendas = agendas;
        c.guiltProbability = guiltProbability;
        c.stayProbability = stayProbability;
        c.prosecutionFacts = prosecutionFacts;
        c.defenseFacts = defenseFacts;
        c.incriminatingFacts = incriminatingFacts;
        c.cameraLocations = cameraLocations;
        c.maxObservations = maxObservations;
        return c;
    }
}
