# COURT — case generator (Gate 1 tool)

Pure Java, no dependencies, no Unity. Prints generated cases to the terminal so you
can read them and decide whether the game works. This is the Phase 1 deliverable from
`COURT_roadmap.md`, and it is throwaway — when the loop is proven it gets rewritten in
C# as `Core/CaseGenerator`.

## Build and run

```bash
powershell -File build.ps1
```

```bash
java -cp bin court.Main --seed 42
```

| Flag | Does |
|---|---|
| `--seed N` | reproduce one exact case; same seed always gives the same case |
| `--case 1..5` | escalation preset from master GDD 3.4 |
| `--count N` | generate N cases |
| `--brief` | one block per case, for scanning fifty |
| `--stats N` | distributions and leak detection over N cases, no cases printed |

Read fifty:

```bash
java -cp bin court.Main --count 50 --brief
```

## What it implements

Master GDD Part III, steps 1–7:

1. Occupancy matrix — constrained random walk over the room adjacency graph
2. Crime placement — scored, not random (see below)
3. Evidence derivation — prints, object relocation, camera coverage
4. Defendant baggage — 2–3 benign reasons he looks guilty, always
5. `Obs(X)` — each character knows only what they could have seen
6. Memory corruption — time shift, person swap, inference promotion
7. Secret agendas — the rare, actual liars

**Step 8, the solvability solver, is deliberately absent.** "Requires ≥3 chained
inferences" is not implementable until we define what an inference step is, and the
pooled testimony is deliberately self-contradictory, so classical entailment won't
work. Decide the formalism after reading real cases, then build it.

## Two things this tool taught us that reading one case cannot

**Crime placement must be scored.** Unconstrained `(t*, l*)` regularly picked a room
nobody visited: one set of prints (case solved instantly) and an innocent defendant
with a perfect alibi (no case at all). Placement now scores every legal pair and
requires a print pool plus a defendant who is entangled with the room.

**Watch the leak table, not the cases.** Any surface feature that correlates with
guilt is a tell, and players will find it. Two real ones were caught this way:

- *Every* innocent case had no eyewitness. `--stats` showed a −30 point drift.
- The perpetrator always left prints, so the defendant's prints being **absent**
  proved innocence with certainty, in ~20% of cases.

Both are invisible when you read one case and fatal across thirty. Run `--stats`
after every tuning change and keep drift under roughly ±12 for anything structural.
Drift on actual evidence (prints, ~±10) is fine — evidence is supposed to inform.

## Where the dials are

`Config.java` — cast size, slots, corruption count, agendas, guilt probability,
how many facts each side is dealt.

`Generator.java` — three constants at the top control the print economy:

| Constant | Meaning |
|---|---|
| `DEFENDANT_PRINT_CHANCE` | innocent defendant printed anyway, when he was in the room earlier |
| `DEFENDANT_PRINT_CHANCE_REMOTE` | same, when he only handled it before the evening |
| `PERPETRATOR_PRINT_CHANCE` | whoever did it left a usable print. Must stay below 1.0 |

The crime placement scorer is in `placeCrime`. The bystander term is weighted above
everything else on purpose — anything that can outbid "was somebody watching" pulls
one guilt branch toward empty rooms and turns the empty room into a verdict.

`World.java` — all flavour. Swap the whole file to re-theme without touching logic.

## Open, for you to decide

- **Case 5 corruption is probably too high.** The GDD's escalation table says 4 false
  memories per witness; with 6 memories per card that makes two thirds of all
  testimony wrong — 22.7 false memories per case. Likely noise rather than deduction.
- **Empty-room cases still drift +24**, but they are only 2.2% of cases, which is
  probably too rare to be learnable. Revisit if playtests say otherwise.
- **Witnesses far from the crime get near-empty cards** and have nothing to say in an
  8-minute trial. Either cut the cast or give distant witnesses something to do.
