# CASE CLOSED — Gate 0.5 Paper Playtest Runbook

**Goal:** find out if generated cases start real arguments — before writing any netcode.
**Pass:** the table argues about who did it, unprompted, for 15+ minutes, and the reveal gets audible reactions.
**You need:** 4–5 players + you as GM. Discord voice. ~45 minutes per case.

## Setup (5 min)

1. Pick a kit from `kits/` (suggested order: `seed-2` → `seed-1` → `seed-15`).
2. Read `gm-sheet.md` yourself. **Never show it to anyone.**
3. Split players: 2 Prosecution, 2 Defense (5th player = the Defendant — give them the
   defendant fragments from the GM sheet's hand section and let them roleplay it).
4. Post `public-brief.md` in the shared channel.
5. DM `prosecution-briefing.md` to prosecution, `defense-briefing.md` to defense.
6. You play: every witness, the lab, the archives, the security console, and the judge.

## Investigation — 15:00 (hard timer, announce at 10, 5, 2, 1)

Teams act in parallel by talking to you (use team DMs/threads for secrecy). Every action costs
real time — tell them the cost, make them wait it out (waiting IS the game):

| Action | Cost | What happens |
|---|---|---|
| Interview a witness | 1 min | Read that witness's opening statement from the GM sheet |
| Follow-up question | 30 s | Answer from the matrix, in character. Corrupted memories STAY corrupted |
| Fetch evidence item | 1 min | Reveal its GM contents line to that team only |
| Lab processing (prints/toxicology) | 90 s wait | Start a timer; deliver the result when it rings |
| Register an item/statement | 30 s | Mark it registered — **only registered things count at trial** |
| Pester a witness a 3rd time | — | They're done. "I have mopping to do." |
| **Offer a witness money** *(optional rule — the bribery system)* | 30 s | Roll d10: 1–4 they cooperate (+1 extra truthful answer) · 5–7 refuse, insulted · 8 pocket the cash, say nothing · 9–10 **ARREST**: that player sits out 90 s and the whole lobby hears why |

Both teams share one world: if a team takes an original item, the other team can only get a
"photo" of it (worth less at trial — say so). If both want the same witness, they wait in line.

## Recess — 2:00

Each team commits, in writing: **up to 4 exhibits + 1 witness to call.** Nothing else exists at trial.

## Trial — 8:00 (you are the judge; be terse, be bored, be terrifying)

| Beat | Time |
|---|---|
| Openings | 30 s per side |
| Prosecution case (present exhibits, question their witness) | 2:00 |
| Defense cross | 1:00 |
| Defense case | 2:00 |
| Prosecution cross | 1:00 |
| Closings | 30 s per side |
| Deliberation — **everyone muted**, count 30 s of silence | 0:30 |
| Verdict | — |

- Each side gets **one objection**. Grounds: Hearsay (did they actually SEE it? — check Obs
  on your sheet), Relevance, Authenticity. Rule instantly from the truth sheet.
- If a witness is pressed on a corrupted memory and the presser cites a contradicting fact,
  the witness cracks: play it ("...I— maybe I only heard the door.").

### Verdict math (cheat table — approximate the judge engine)

Score each side's registered, surviving exhibits: **direct physical = 3 · document/CCTV = 2 ·
witness statement = 1 · anything successfully objected = 0.** Photos count half.
**Guilty if Prosecution ≥ Defense × 1.25.** Within ~1 point of the line: **MISTRIAL** (chaos; enjoy).

## The Reveal (the whole point — do not rush this)

Read slowly, in order:
1. The story: walk the occupancy matrix as narrative. "At 9:30, three people entered the
   Wedding Hall. One of them left with the vat."
2. **GUILTY / INNOCENT** — and who actually did it.
3. The corruption ledger: every false memory, every deleted sighting, who was protecting whom.
4. The defendant's hand fidelity — which CLEAR memories were real.
5. The proof chain — what a perfect investigation would have found.
6. End on the cruelest true sentence the case gives you. ("Greg saw everything. Nobody
   asked him the right question.")

Then fill in `RESULTS-FORM.md`. One form per case. No skipping.

## GM principles

- You are the building, not a storyteller: answer what they ask, volunteer nothing except
  witness personality flavor.
- Witnesses believe their corrupted memories completely. Never wink.
- When in doubt, check the matrix. The matrix is always right.
- If the table goes quiet for 60+ seconds, note it on the form — silence is data.
