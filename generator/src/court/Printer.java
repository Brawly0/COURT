package court;

import court.Model.*;

import java.util.List;

/** Renders a generated case for a human to read. This is the whole Gate 1 tool. */
public final class Printer {

    private final StringBuilder sb = new StringBuilder();

    public String full(CourtCase c) {
        sb.setLength(0);
        header(c);
        groundTruth(c);
        rule('=');
        line("PLAYER CARDS  --  hand these out, nothing else");
        rule('=');
        defendantCard(c);
        prosecutionCard(c);
        defenseCard(c);
        witnessCards(c);
        judgeCard();
        return sb.toString();
    }

    /** One block per case, for scanning fifty of them. */
    public String brief(CourtCase c) {
        sb.setLength(0);
        String verdict = c.crime.defendantGuilty() ? "GUILTY" : "INNOCENT";
        line(String.format("seed %-6d  %s %s %s at %s   |   did it: %s   |   defendant %s is %s",
                c.seed,
                c.crime.objectName(),
                c.crime.verb(),
                c.loc(c.crime.location()),
                World.SLOT_LABELS[c.crime.slot()],
                c.person(c.crime.perpetrator()).name(),
                c.defendant().name(),
                verdict));

        int corrupt = 0;
        for (List<Observation> obs : c.observations.values()) {
            for (Observation o : obs) if (o.corrupt()) corrupt++;
        }
        StringBuilder eyes = new StringBuilder();
        for (int p = 0; p < c.cast.size(); p++) {
            if (c.occupancy[p][c.crime.slot()] == c.crime.location() && p != c.crime.perpetrator()) {
                if (eyes.length() > 0) eyes.append(", ");
                eyes.append(c.person(p).name());
            }
        }
        line("          in the room: " + (eyes.length() == 0 ? "nobody -- no eyewitness" : eyes));
        line("          false memories in play: " + corrupt + "   agendas: " + c.agendas.size()
                + "   prints: " + names(c, c.printsOnObject));
        line("");
        return sb.toString();
    }

    // ------------------------------------------------------------------

    private void header(CourtCase c) {
        rule('=');
        line("COURT -- generated case");
        line("seed " + c.seed + "   cast " + c.cfg.characters + "   slots " + c.cfg.slots
                + "   corruptions/witness " + c.cfg.corruptionsPerWitness
                + "   agendas " + c.cfg.agendas);
        line(c.venue);
        rule('=');
        line("");
    }

    private void groundTruth(CourtCase c) {
        rule('-');
        line("GROUND TRUTH  --  no player ever sees any of this");
        rule('-');

        int width = 14;
        StringBuilder head = new StringBuilder(pad("", width));
        for (int t = 0; t < c.cfg.slots; t++) head.append(pad(World.SLOT_LABELS[t], 18));
        line(head.toString());

        for (Person p : c.cast) {
            StringBuilder row = new StringBuilder(pad(p.name() + (p.defendant() ? " *" : ""), width));
            for (int t = 0; t < c.cfg.slots; t++) {
                String cell = c.loc(c.occupancy[p.id()][t]).replace("the ", "");
                if (t == c.crime.slot() && c.occupancy[p.id()][t] == c.crime.location()) cell = "[" + cell + "]";
                row.append(pad(cell, 18));
            }
            line(row.toString());
        }
        line("");
        line("* = defendant   [ ] = in the room when it happened");
        line("");

        line("THE CRIME");
        line("  " + c.crime.objectName() + " " + c.crime.verb() + " "
                + c.loc(c.crime.location()) + " at " + World.SLOT_LABELS[c.crime.slot()] + ".");
        line("  It ended up in " + c.loc(c.crime.objectMovedTo()) + ".");
        line("  Perpetrator: " + c.person(c.crime.perpetrator()).name());
        line("  DEFENDANT IS " + (c.crime.defendantGuilty() ? "GUILTY" : "INNOCENT"));
        line("");

        line("CAMERAS");
        StringBuilder cams = new StringBuilder("  covered: ");
        for (int l = 0; l < World.LOCATION_COUNT; l++) {
            if (c.cameraCovered[l]) cams.append(c.loc(l)).append("  ");
        }
        line(cams.toString());
        line("");

        line("PRINTS ON " + c.crime.objectName().toUpperCase());
        line("  " + names(c, c.printsOnObject));
        line("");

        line("FALSE MEMORIES  --  the witness believes these are true");
        boolean any = false;
        for (Person p : c.cast) {
            for (Observation o : c.observations.get(p.id())) {
                if (!o.corrupt()) continue;
                any = true;
                line("  " + pad(p.name(), 12) + o.corruption + " -- " + o.truthNote);
            }
        }
        if (!any) line("  (none)");
        line("");

        if (!c.agendas.isEmpty()) {
            line("DELIBERATE LIARS");
            for (Agenda a : c.agendas) {
                line("  " + pad(c.person(a.owner()).name(), 12) + a.label() + " -- " + a.instruction());
            }
            line("");
        }
    }

    private void defendantCard(CourtCase c) {
        cardHeader("THE DEFENDANT -- " + c.defendant().name() + " (" + c.defendant().descriptor() + ")");
        line("  YOU " + (c.crime.defendantGuilty() ? "DID IT." : "DID NOT DO IT.")
                + "  Nobody else knows this, including your own lawyer.");
        line("");
        line("  Where you were:");
        for (int t = 0; t < c.cfg.slots; t++) {
            line("    " + World.SLOT_LABELS[t] + "   " + c.loc(c.occupancy[0][t]));
        }
        line("");
        line("  Why you look bad:");
        for (String b : c.defendantBaggage) line("    - " + b);
        line("");
    }

    private void prosecutionCard(CourtCase c) {
        cardHeader("PROSECUTION");
        line("  " + c.crime.objectName() + " " + c.crime.verb() + " "
                + c.loc(c.crime.location()) + ". The defendant is "
                + c.defendant().name() + ".");
        line("");
        line("  What forensics gave you:");
        for (Fact f : c.prosecutionFacts) line("    - " + f.text());
        line("");
        line("  You do not know whether he did it.");
        line("");
    }

    private void defenseCard(CourtCase c) {
        cardHeader("DEFENSE ATTORNEY");
        line("  Your client is " + c.defendant().name() + ". You do not know whether he did it.");
        line("  He may tell you. He may lie to you. That is his decision, not yours.");
        line("");
        line("  What you managed to obtain:");
        for (Fact f : c.defenseFacts) line("    - " + f.text());
        line("");
    }

    private void witnessCards(CourtCase c) {
        for (Person p : c.cast) {
            if (p.defendant()) continue;
            cardHeader("WITNESS -- " + p.name() + " (" + p.descriptor() + ")");

            line("  Where you were:");
            for (int t = 0; t < c.cfg.slots; t++) {
                line("    " + World.SLOT_LABELS[t] + "   " + c.loc(c.occupancy[p.id()][t]));
            }
            line("");
            line("  What you remember:");
            List<Observation> obs = c.observations.get(p.id());
            if (obs.isEmpty()) {
                line("    - Nothing useful. You were not near any of it.");
            } else {
                for (Observation o : obs) line("    - " + render(c, o));
            }

            for (Agenda a : c.agendas) {
                if (a.owner() == p.id()) {
                    line("");
                    line("  PRIVATE: " + a.instruction());
                }
            }
            line("");
        }
    }

    private void judgeCard() {
        cardHeader("JUDGE");
        line("  Nothing. You get nothing. That is the point.");
        line("");
    }

    /** Renders as the witness believes it -- corruption is invisible here by design. */
    private String render(CourtCase c, Observation o) {
        String at = " at " + World.SLOT_LABELS[o.slot] + ".";
        return switch (o.kind) {
            case SAW_PERSON -> "You saw " + c.person(o.other).name() + " in "
                    + c.loc(o.location) + at;
            case HEARD_NOISE -> "You heard something from " + c.loc(o.location) + at;
            case SAW_LEAVE -> "You saw " + c.person(o.other).name() + " leaving "
                    + c.loc(o.location) + " in a hurry" + at;
        };
    }

    // ------------------------------------------------------------------

    private String names(CourtCase c, List<Integer> ids) {
        if (ids.isEmpty()) return "no usable prints recovered";
        StringBuilder s = new StringBuilder();
        for (int id : ids) {
            if (s.length() > 0) s.append(", ");
            s.append(c.person(id).name());
        }
        return s.toString();
    }

    private void cardHeader(String title) {
        rule('-');
        line(title);
        rule('-');
    }

    private void rule(char ch) { line(String.valueOf(ch).repeat(96)); }

    private void line(String s) { sb.append(s).append(System.lineSeparator()); }

    private static String pad(String s, int w) {
        if (s.length() >= w) return s + " ";
        return s + " ".repeat(w - s.length());
    }
}
