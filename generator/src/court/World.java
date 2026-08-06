package court;

/**
 * All flavour. The generator does not read meaning into any of these strings —
 * swap this file wholesale to re-theme the game without touching the logic.
 *
 * Design rule from the GDD: comedy on top, rigor underneath. The timeline is
 * serious. The crime is that someone stole 400kg of hummus.
 */
public final class World {

    /**
     * Five rooms, always in this index order, so ADJ below applies to every venue:
     *   0 <-> 1, 3     1 <-> 0, 2     2 <-> 1, 4     3 <-> 0, 4     4 <-> 2, 3
     * which is the ring 0-1-2-4-3-0.
     */
    public record Venue(String name, String[] rooms, String[][] crimes) {}

    public static final Venue[] VENUES = {

        new Venue("Qasr al-Farah Wedding Hall",
            new String[]{"the Kitchen", "the Main Hall", "the Courtyard", "the Storage Room", "the Back Alley"},
            new String[][]{
                {"400 kilos of hummus",          "were taken from"},
                {"the mahr envelope",            "was emptied in"},
                {"the four-tier wedding cake",   "was destroyed in"},
                {"the bride's gold",             "was lifted from"},
                {"the hall's generator",         "was sabotaged in"},
                {"the entire sound system",      "was unplugged mid-song in"}
            }),

        new Venue("Al-Nawras Public Pool",
            new String[]{"the Snack Bar", "the Pool Deck", "the Changing Rooms", "the Pump Room", "the Car Park"},
            new String[][]{
                {"the lifeguard's whistle",      "was stolen from"},
                {"sixty litres of chlorine",     "were poured out in"},
                {"the filter pump",              "was sabotaged in"},
                {"the ice cream freezer",        "was unplugged in"},
                {"every single pair of goggles", "was cleared out of"}
            }),

        new Venue("the Haddad house, third day of the aza",
            new String[]{"the Kitchen", "the Men's Salon", "the Women's Salon", "the Upstairs Landing", "the Front Garden"},
            new String[][]{
                {"the coffee urn",               "was emptied in"},
                {"two hundred rented chairs",    "were removed from"},
                {"the condolence box",           "was opened in"},
                {"the late Abu Nabil's radio",   "was taken from"},
                {"eleven trays of ma'moul",      "were eaten in"}
            }),

        new Venue("Abu Fadi Tyres & Alignment",
            new String[]{"the Office", "the Workshop", "the Forecourt", "the Parts Store", "the Street Side"},
            new String[][]{
                {"a brand new set of tyres",     "was rolled out of"},
                {"the compressor",               "was sabotaged in"},
                {"the workshop cat",             "was let out of"},
                {"the till float",               "was emptied in"},
                {"somebody's entire gearbox",    "was removed from"}
            }),

        new Venue("Al-Rajaa Secondary, exam day",
            new String[]{"the Staff Room", "the Main Corridor", "the Playground", "the Supply Cupboard", "the Front Gate"},
            new String[][]{
                {"the exam papers",              "were opened in"},
                {"the bell",                     "was disconnected in"},
                {"the headmaster's chair",       "was taken from"},
                {"forty confiscated phones",     "were released from"},
                {"the canteen's entire ka'ak",   "was cleared out of"}
            }),

        new Venue("the Sharqi Street minibus depot",
            new String[]{"the Ticket Window", "the Waiting Hall", "the Yard", "the Fuel Shed", "the Street"},
            new String[][]{
                {"the day's fares",              "were emptied from"},
                {"an entire minibus",            "was driven off from"},
                {"the depot's only clipboard",   "was taken from"},
                {"eighty litres of diesel",      "were siphoned from"},
                {"the schedule board",           "was rearranged in"}
            })
    };

    /** Ring adjacency. Adjacency = who can overhear whom, and where you can walk in one slot. */
    public static final int[][] ADJ = {
        {3, 1},  // 0 <-> 3, 1
        {0, 2},  // 1 <-> 0, 2
        {1, 4},  // 2 <-> 1, 4
        {0, 4},  // 3 <-> 0, 4
        {2, 3}   // 4 <-> 2, 3
    };

    public static final int LOCATION_COUNT = 5;

    public static final String[] SLOT_LABELS = {
        "20:00", "20:30", "21:00", "21:30", "22:00", "22:30"
    };

    public static final String[] NAMES = {
        "Abu Samir", "Um Khalil", "Rami", "Nadia", "Jad",
        "Hisham", "Lina", "Tareq", "Ghassan", "Maya"
    };

    /**
     * Descriptors are assigned so that exactly two people share each one.
     * That is what makes the PERSON_SWAP memory corruption plausible rather than absurd.
     */
    public static final String[] DESCRIPTORS = {
        "grey jacket",
        "dark kufiyyeh",
        "tall and thin",
        "carrying a tray",
        "red headscarf"
    };

    /** Red herrings. People touch these too. */
    public static final String[] DECOY_OBJECTS = {
        "a stack of folding chairs",
        "somebody's laptop",
        "a tray of baklava",
        "the guestbook",
        "the ice machine key",
        "a mop that belongs to nobody",
        "an unattended thermos",
        "three unclaimed jackets",
        "a fire extinguisher last serviced in 2014",
        "a plastic bag of receipts"
    };

    /** Why the defendant will not say where he was. All unrelated to the crime. */
    public static final String[] EMBARRASSING = {
        "you were smoking on the roof and your mother was directly below you",
        "you were on the phone with someone your family has opinions about",
        "you were eating from the buffet before it opened, standing up, with your hands",
        "you were hiding from your uncle, who wants his money back",
        "you were asleep in a plastic chair for twenty minutes",
        "you were crying in a parked car about something entirely unrelated",
        "you were practising a speech into a mirror",
        "you were losing an argument with a vending machine",
        "you were watching the match on your phone with the sound on",
        "you had taken your shoes off and then could not find them",
        "you were arguing with your brother about a debt from 2019",
        "you were in the bathroom for a length of time you will not disclose",
        "you were trying to fix your hair and it was not going well",
        "you were avoiding a woman who believes you are engaged to her daughter",
        "you were eating a second dinner",
        "you were on hold with the electricity company",
        "you had gone out to your car to eat crisps in peace",
        "you were being lectured about your career by someone you had just met",
        "you were googling whether you were legally allowed to leave",
        "you were rehearsing how to say no to a favour"
    };

    /** Motive dressing. Always plausible, never proof. */
    public static final String[] MOTIVES = {
        "everyone knows you were not paid for last month's work here",
        "you argued loudly with the family two weeks ago and people remember it",
        "you have been telling anyone who listens that this place overcharges",
        "your brother was let go from here in the spring",
        "you needed money badly and several people knew it",
        "you were passed over for something and did not take it well",
        "you told at least four people you would do something about it",
        "you have a documented history with this exact object",
        "you lost a bet last month and have not paid",
        "something almost identical happened in 2021 and was never resolved",
        "your cousin runs the competing place across the road",
        "you were heard saying the word 'watch' followed by the word 'this'"
    };

    public static final String[] AGENDA_AFFAIR = {
        "You were somewhere you cannot admit to being. Do not say where you really were at %s.",
        "You were meeting someone privately at %s. You will lie about your location rather than explain.",
        "At %s you were doing something legal, humiliating, and unmentionable. Deny the location."
    };

    public static final String[] AGENDA_PROTECT = {
        "%s is family. If anyone asks whether you saw them near %s, you did not.",
        "%s covered for you once. You will not put them at %s, no matter what you actually saw.",
        "%s cannot survive being involved in this. Keep them out of %s."
    };

    public static final String[] AGENDA_GRUDGE = {
        "You cannot stand the defendant. You will claim you saw them at %s at %s. You did not.",
        "The defendant humiliated you in front of people. Put them at %s at %s and do not back down.",
        "You have waited a long time for this. Place the defendant at %s at %s and say it calmly."
    };

    public static boolean adjacent(int a, int b) {
        for (int x : ADJ[a]) if (x == b) return true;
        return false;
    }

    private World() {}
}
