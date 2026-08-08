# COURT — Map & Run Design v1

> Designed against the **actual build**, not the GDD's aspirations. Every number below
> is derived from code in this repo or from the scene hierarchy as it exists today.

## 0. The constants everything hangs off

| Constant | Value | Source |
|---|---|---|
| Walk speed | 3.5 m/s | `FirstPersonController.cs:14` |
| Sprint speed | 6.0 m/s | `FirstPersonController.cs:15` |
| Stamina / drain | 100 @ 15 per sec | `FirstPersonController.cs:21` |
| **Sprint range** | **~40 m, then you walk** | 6.7 s × 6.0 m/s |
| Investigation | 900 s | `CaseRuntime.cs:17` |
| Core footprint | 48 m × 48 m, 12 m atrium | commit `c272023` |
| Rooms existing | 18 | scene hierarchy |

**The load-bearing fact: a full stamina bar covers ~40 m and the core is 48 m.**
Nobody can sprint across this building. Sprinting is a burst you spend to escape a
room, win a race to a terminal, or reach the courtroom before the bell — never a
travel mode. Every distance below is tuned so that sprinting is a *decision*.

### The time budget, which is the real design

```
900 s  ÷  20 room visits  =  45 s per visit
                              ├─ ~15 s travel
                              └─ ~30 s doing the thing
```

Interaction costs (GDD 5.3): Interview 20 s · Bribe 30 s · Subpoena 25 s ·
Delay 35 s · Coach 40 s · Lab processing 30–60 s.

So **18–22 visits per player per case** is the target, and it falls out of the
arithmetic rather than being asserted. If average travel creeps to 25 s, visits drop
to ~14 and the run feels like a walking simulator. If it drops to 8 s, players do
everything and there is no triage. **Travel time is the difficulty dial.**

---

## 1. The run, step by step

```
  LOBBY            Roles dealt. Defendant learns the truth, alone, on his card.
    ↓  0:30
  BRIEFING         Both teams enter their own office. Prosecution gets 1 forensic
    ↓              fact. Defense gets nothing and has to ask their client.
  INVESTIGATION    15:00. The building unlocks. Courtrooms stay sealed.
    ↓              gather → process → register / coach → bribe → sabotage
  THE BELL         Courtroom doors open, everything else locks. 20 s to be seated
    ↓              or you are tried in absentia.
  TRIAL            8:00. Floor control. Only registered evidence is admissible.
    ↓
  VERDICT          Judge rules against the fact-graph.
    ↓
  THE REVEAL       True timeline plays, overlaid with what each team actually did.
    ↓
  INTEGRITY        Shared meter moves. Run continues or the courthouse closes.
```

### The three pressure waves

A 15-minute free-roam phase goes flat in the middle. The map fixes this with
**scheduled events that change what the building is**, not with more rooms:

| Time | Event | What it does to the map |
|---|---|---|
| **15:00–10:00** | *Open season* | All routes live. Teams sweep for evidence. |
| **10:00** | **Witnesses arrive** | The Lounge fills. Every witness action becomes available at once — the map's centre of gravity shifts to Floor 1. |
| **05:00** | **Night lighting** | Half the corridor lights drop. Cameras still record, but *sightlines* die. Sabotage becomes viable; before this, you're too visible. |
| **02:00** | **Clerk's cut-off** | Registration closes in 2:00. Everyone converges on the Evidence Locker at once. Guaranteed collision. |
| **00:00** | **The bell** | Courtroom opens, everything else seals. |

The 02:00 cut-off is the single most important one. It forces both teams into the
same room at the same time with things they don't want each other to see.

---

## 2. Layout philosophy — the three-corridor rule

Real courthouses are built around **separated circulation**. A federal courthouse
has three route networks that never mix:

1. **Public** — lobby, atrium, main stairs. Watched, slow, everyone.
2. **Judicial / staff** — behind the offices, keycard only, direct.
3. **Custody** — sally port → holding → dock. Never touches the other two.

**Import this wholesale. It is the best real-world idea available to this game**,
because it turns "which way do I go" into a real decision with real trade-offs:

| Route | Speed | Cameras | Who |
|---|---|---|---|
| **Public spine** | Slow (must lap the atrium) | Full coverage | Everyone |
| **Staff corridor** | ~35 % faster, direct | Sparse | Needs a keycard — one per team, stealable |
| **Custody run** | Fastest vertical in the building | None | Defendant is *forced* onto it; others need the bailiff's key |

The Defendant's ankle monitor bars him from the Evidence Locker and Chambers and
alarms in restricted wings — so he is pushed onto the custody route, which is fast
but goes to all the wrong places. He can move quickly between places he cannot use.
That is the correct shape for the one player who knows the truth and can act on it
least.

---

## 3. The building

48 m × 48 m core, 12 m atrium void through all three floors, basement below,
parking garage attached by a link corridor. Coordinates below are metres from the
atrium centre.

```
        ATRIUM VOID   -6 .. +6        (visible from every floor — movement is information)
        RING CORRIDOR  6 .. 10        (4 m wide, public, fully cameraed)
        ROOM BAND     10 .. 24        (14 m deep — rooms open off the ring)
        STAFF SPUR    behind rooms    (keycard, sparse cameras, cuts corners)
```

### Ground floor — "the public floor"

```
                        N
   ┌──────────────────────────────────────────────┐
   │  PRESS ROOM     │  MAIN ENTRANCE │ SECURITY  │
   │                 │   + lobby      │  OFFICE   │
   ├─────────────────┴────────────────┴───────────┤
   │        ╔═══════════════════════════╗         │
   │ CAFE-  ║                           ║  EVIDENCE│
 W │ TERIA  ║       ATRIUM VOID         ║  LOCKER  │ E
   │        ║   grand stair @ N edge    ║          │
   │        ╚═══════════════════════════╝         │
   ├──────────────────────────────────┬───────────┤
   │        COURTROOM A               │ COURTROOM │
   │      (sealed until the bell)     │     B     │
   └──────────────────────────────────┴───────────┘
                        S
        ↓ stairs to basement (single point, NW corner)
        → garage link corridor (E, off Evidence Locker)
```

### Floor 2 — "the working floor"

```
   ┌──────────────────────────────────────────────┐
   │  PROSECUTION OFFICE  │    │  DEFENSE OFFICE  │   ← north wing, opposite ends
   ├──────────────────────┤    ├──────────────────┤     of the same corridor
   │                 ATRIUM VOID  (rail)          │
   │   west link  ═══════════════════  east link  │
   ├──────────────────────┬────┬──────────────────┤
   │      ARCHIVES        │    │   RECORDS ROOM   │   ← south wing
   └──────────────────────┴────┴──────────────────┘
```

### Floor 3 — "the quiet floor"

```
   ┌──────────────────────────────────────────────┐
   │   JUDGE'S CHAMBERS   │    │   STAFF ROOM     │
   ├──────────────────────┤    ├──────────────────┤
   │                 ATRIUM VOID  (rail)          │
   ├──────────────────────┬────┬──────────────────┤
   │    FORENSICS LAB     │    │  WITNESS LOUNGE  │
   └──────────────────────┴────┴──────────────────┘
```

### Basement & garage

```
   BASEMENT:   BOILER ROOM ── MAINTENANCE ── HOLDING CELLS
                    │                              │
               (no cameras)                  (custody route up
                                              to the courtroom dock)

   GARAGE:     PARKING GARAGE ── link corridor ── Evidence Locker (ground)
               sally port, one camera at the gate only
```

---

## 4. Every room: verb, story, risk

Rooms already exist in `Courthouse.unity`. This assigns each one a **verb**, a
**reason to exist in fiction**, and a **risk** — the GDD's "every room is a verb"
rule, applied to the real 18.

### Ground floor

| Room | Verb | The story behind it | Risk |
|---|---|---|---|
| **Main Entrance / Lobby** | Spawn, regroup | Metal detector nobody staffs. The one place both teams must pass. | Total visibility. Everything you carry is seen. |
| **Security Office** | *Watch* | Bank of monitors, a guard who left for a smoke in 2019. | Feeds are live — but the room's own camera records you watching. Wiping footage leaves a gap. |
| **Evidence Locker** | *Register* | Cage, clipboard, chain-of-custody log. | Registration is **public** — the other team sees *that* you logged something, never what. |
| **Press Room** | *Leak* | Folding chairs, a lectern, a permanently open phone line. | Leaking a fact to the press makes it admissible without registration — but the judge's bias card may punish trial-by-media. |
| **Cafeteria** | *Talk* | Vending machine, three working tables, terrible coffee. | The only room with **no camera and no consequence**. Neutral ground where deals get made — and where the other team can watch you make them. |
| **Courtroom A / B** | *Endgame* | A is grand and ceremonial; B is the overflow room with a stained ceiling. | Sealed until the bell. **Which courtroom you're assigned is announced at 02:00** — B is 20 s further from the Locker. |

### Floor 2

| Room | Verb | The story behind it | Risk |
|---|---|---|---|
| **Prosecution Office** | *Base* | State's desk, case files, a wall of old convictions. | Team spawn. Anything left here is safe — and the staff corridor runs right behind it. |
| **Defense Office** | *Base* | Rented desk, one lamp, a client who may be lying to you. | Same, mirrored. Deliberately at the **opposite end of one corridor** — see §5. |
| **Archives** | *Search* | Physical files by name and date. Slow, dusty, indexed badly. | Files can be **burned** (loud, leaves an empty folder) or **misfiled** (quiet, recoverable, buys 4 minutes). |
| **Records Room** | *Cross-reference* | Financial records, payment logs, employment history. | Where **bribes surface**. Bribe a witness and the payment record lands here for anyone to find. |

### Floor 3

| Room | Verb | The story behind it | Risk |
|---|---|---|---|
| **Forensics Lab** | *Process* | One machine. 30–60 s per item, and it runs in real time. | **Someone must stand there while it runs.** Machine is jammable. One machine, two teams — see §5. |
| **Witness Lounge** | *Interview / coach / bribe / delay* | Vinyl chairs, a water cooler, people who would rather be anywhere else. | Everything you do here is seen **by the other witnesses**. Over-coach and they crack under cross. |
| **Judge's Chambers** | *Bribe* | Robes on a hook, a decanter, a man who is bored. | No cameras. Catastrophic if exposed. Defendant is barred and alarms on entry. |
| **Staff Room** | *Steal the keycard* | Lockers, a rota, someone's lunch. | The **staff-corridor keycard** spawns here. One per team per case. Stealing the other team's is the cleanest theft in the game. |

### Basement & garage

| Room | Verb | The story behind it | Risk |
|---|---|---|---|
| **Boiler Room** | *Destroy* | Furnace, asbestos warning, no camera. | The only true destruction point. But a burned file leaves an **empty folder** upstairs, and the boiler logs a burn cycle. |
| **Maintenance** | *Cut the power* | Breakers, fuse box, mop sink. | Kills lighting **and** cameras on one floor for 60 s. Everyone sees the lights die — they just don't know who did it. |
| **Holding Cells** | *Hold* | Three cells, a bench, a bailiff's desk. | Where contempt sends you during the trial. Also the custody-route entrance. |
| **Parking Garage** | *Arrive / hide* | Sodium light, oil stains, one working camera at the gate. | Furthest point in the building. Things hidden in a car are safe — if you have time to come back. |

---

## 5. Making the teams collide

The GDD's own risk register flags **"teams split the map and never interact"** as
a medium risk. A big building makes that worse, not better. Four structural fixes,
in order of how much they matter:

**1. One lab machine.** Not two. One. Processing takes 30–60 s and requires
presence. Two teams, one machine, and a queue is a confrontation with a timer on it.

**2. Offices at opposite ends of one corridor.** Prosecution and Defense both sit
on the F2 north wing. Every trip either team makes to their own base passes the
other's door. They will see each other constantly and learn nothing — which is
exactly the tension: you know *that* they moved, never *why*.

**3. The 02:00 registration cut-off.** Everything must be registered in the
Evidence Locker before it closes. Both teams converge on one cage in the last two
minutes, holding things they don't want seen.

**4. One archive terminal.** The index is a single machine. You can search the
shelves by hand — but it takes three times as long.

**Deliberate counterweight — the Cafeteria.** One room with no camera and no
mechanical consequence. Teams need somewhere to negotiate, gloat, and make deals
that the game does not adjudicate. Every good social game needs a room where the
systems stop and the people start.

---

## 6. Walk-time matrix — MEASURED, not targeted

> **Correction.** An earlier draft of this document quoted the GDD's *target* walk
> times as if the build met them. It does not. Below are distances computed from
> the actual room coordinates in `GrayboxBuilder.cs` (lines 74–93) and the stair
> stack (lines 112–118). **The building is roughly 2–3× smaller than the GDD's
> pacing assumed.** That is a finding, not a failure — see the verdict below.

### Actual geometry

```
Building shell   x −24.75 … 24.75   (49.5 m)      Shell_E / Shell_W
                 z −20.75 … 22.75   (43.5 m)      Shell_N / Shell_S
Central hall     z −6 … 6 (12 m deep) × 48 m wide  ← NOT a square atrium.
                                                     It is one long E–W hall.
Room band        z 6 … 20  (north)  and  z −20 … −6 (south)
Floors           y = −4 (basement), 0, 4, 8
```

Every floor is **four rooms**: two north (doors on z = 6), two south (doors on
z = −6), opening onto the same central hall. Doors sit at x = ±12 or ±13.

**The stair stack is the whole vertical circulation** and it is a single well:

| Flight | From → To | Run |
|---|---|---|
| `Stairs_B_to_G` | (−14, −4) → (−2, 0) | 12 m |
| `Stairs_G_to_F2` | (−14, 0) → (−2, 4) | 12 m |
| `Landing_F2` | x −2 … 3, z −2.5 … 2.5 | — |
| `Stairs_F2_to_F3` | (3, 4) → (15, 8) | 12 m |

### Measured times at walk speed (3.5 m/s)

| Route | Real path | **Actual** | GDD target | Gap |
|---|---|---|---|---|
| Hall centre → CourtroomA | 8.5 m | **2.4 s** | 8–12 s | 4× short |
| Hall centre → Cafeteria | 16.2 m | **4.6 s** | 8–12 s | 2× short |
| Hall centre → F2 room | 44 m | **12.6 s** | 15–20 s | close |
| Hall centre → F3 room | 51 m | **14.6 s** | 15–20 s | **on target** |
| Hall centre → Basement room | 41 m | **11.8 s** | 25–30 s | 2× short |
| **Archives (F2) → Lab (B)** | 61 m | **17.5 s** | ~35 s | 2× short |
| Hall centre → Parking Garage | 38 m | **10.9 s** | — | furthest point |

### The verdict — do NOT scale the building up

The instinct is to make it bigger. Resist it. Run the budget:

```
900 s ÷ (≈10 s travel + ≈30 s action) = ~22 visits per player
```

**22 visits is inside the 18–22 target band.** The pacing is already right, because
interaction times — not distances — dominate the budget. Doubling the building
would push travel to ~20 s, drop visits to ~18, and add nothing but walking.

What the small footprint actually costs is **triage pressure**. When everything is
10 s away you never have to choose what to skip. Fix that with *friction that isn't
distance*:

- **Two vertical cores, both at the ends** *(BUILT — see §10)*. The single atrium
  staircase was removed. It read as civic ceremony ("be seen") in a game about
  hiding things, it made every journey identical, and its two flights occupied the
  same footprint so you had to walk backwards around an open well to change floors.
  Replaced by a **public west core** (open, tiled, overlooked) and a **service east
  core** (concrete, enclosed, the only route to the basement). Which stair someone
  took is now information.
- **One lab machine, occupancy-locked** (§5) — a 60 s process nobody else can start.
- **The 02:00 registration cut-off** (§1) — the real scarcity is time, not metres.
- **Door interaction delays** — 1.5 s to open a locked door beats 15 m of corridor.

**Revised sprint role.** With 40 m of sprint range against a 61 m longest route,
sprint covers roughly two thirds of the worst journey. It stays a meaningful
decision — you can win one race per stamina bar, not cross the building at will.
That still works. No change needed.

**Consequence for §8:** the staff-corridor shortcut is now a *bad* idea. Saving
35 % on a 12 s trip is 4 s — not worth the build cost, and it would weaken the
stair chokepoint that is doing the real work. **Cut it.** Keep the custody route
(it exists for the Defendant's role, not for speed).

---

## 7. Outside the building — atmosphere

The courthouse should feel like a place that has processed 40,000 cases and cared
about none of them. **Municipal dread**: fluorescent light, water-stained ceiling
tile, laminate wood, forms in triplicate. Comedy comes from absurd content inside a
straight-faced institutional shell — the *Papers, Please* trick.

**Immediately outside**, visible through the lobby glass and the F3 windows:

- A **square with a dry fountain** and a statue of someone local whose plaque has
  been stolen. Pigeons. Three parked taxis that never move.
- A **row of shopfronts** across the street: a photocopy shop, a stationer selling
  legal forms, a café with plastic chairs on the pavement. All closed, all lit.
- **A wedding hall two streets over**, sign half-lit — the crime scene from the
  generator's most common archetype. You can see it from the F3 lab window. You can
  never go there. *The crime is always somewhere you cannot reach.*
- **Weather is per-case, seeded.** Rain hammering the atrium skylight changes the
  audio profile of the entire building and masks footsteps. Tie it to the case seed
  so a run has a mood.

**Time of day is fixed at dusk** and does not advance — except the 05:00 lighting
drop, which reads as the building's automatic timer, not as sunset. Committing to
one lighting state means one bake, which matters for a solo art pass.

**Sound is the real atmosphere.** Fluorescent hum, distant typing, HVAC, a phone
ringing in an empty office. Footsteps on four surfaces, because **footsteps are
information** — marble in the atrium (loud, carries), carpet in the offices
(silent), concrete in the basement (echoing), metal stairs (unmistakable). A player
who learns the floor surfaces can track the other team by ear. That is a skill
ceiling worth having, and it costs four sound sets.

**Silence during the trial.** Player voices are the soundtrack of that phase.

---

## 10. BUILT — what actually shipped into `GrayboxBuilder.cs`

Implemented and verified in `Courthouse.unity` and in the packaged `level0`:

| Change | Why |
|---|---|
| **3 m corridors on every floor** (north rooms start `z=9`, south rooms end `z=−9`) | **F2 and F3 were impassable.** The wing slab was `z 6…20` and the rooms were *also* `z 6…20` — the rooms *were* the wing. `Archives`' door opened onto a rail over the atrium void, and `Bridge_F2_North` landed on the shared wall between two rooms. There was no route from the stairs to any upper-floor room. |
| **West/East links narrowed to the inner 3 m**, extended to `z ±9` | Closes the floor ring; the outer 3 m becomes stairwell. |
| **Central staircase deleted**, replaced by two end cores | See §5. Flights are separated along `z` so none passes under another — the old switchback had ~1.3 m headroom where the flights crossed. |
| **`Hall_Floor`** — one slab instead of four | `Hall_Floor_W/N/S` existed only to work around the removed stairwell. |
| **`Lobby_South`** (`x −6…6, z −20…−9`) | Unfloored gap between Security and Cafeteria you could walk into. |
| **Garage duplicate walls removed** | `BuildRoom` already builds the garage's four walls with a 3 m door; five extra `WallSeg` calls duplicated three of them coplanar (z-fighting) and added a second east wall with a 6 m opening, leaving a fragment across the doorway. |

Still unverified by play: whether the west and east cores *feel* meaningfully
different to walk. That is the whole premise of the watched/unwatched split, and
only a session settles it.

## 8. What this changes about the current build

Omar's 18 rooms are the right rooms. Almost nothing needs demolishing. What's
missing is **routing and rules**, not geometry:

| Change | Effort | Why |
|---|---|---|
| Add the **staff corridor** behind the F2/F3 room bands | Medium | Creates the second circulation network. Without it there is one way everywhere and no route decisions. |
| Add the **custody stair** (Holding → courtroom dock) | Small | Third network. Gives the Defendant his forced, fast, useless route. |
| Move **Defense Office** to share the F2 north wing with Prosecution | Small | Forced proximity. Currently they can avoid each other all game. |
| Make the **Lab a single machine** with an occupancy lock | Small | The best contested resource in the design. |
| Walk-test with a stopwatch against §6 | Small | Cheapest possible validation, and it's the whole point. |
| Add **Cafeteria camera exclusion** | Trivial | One room where the systems stop. |
| Seal **Courtroom B** and announce assignment at 02:00 | Small | Turns the last two minutes into a scramble. |

**Validate before building.** Walk every route in §6 with a stopwatch before adding
a single wall. If Archives → Lab isn't ~35 s, the room band is the wrong depth, and
that is a five-minute fix now and a two-week fix after the art pass.

---

## 9. Open questions

- **Is 48 m too small?** To hit 10 s ground-floor times at 3.5 m/s you need ~35 m
  of path. The ring topology inflates straight-line distance enough to work — but
  this is the first thing to measure, not assume.
- **Does the Defendant's forced custody route feel powerful or humiliating?**
  It should feel like both. Playtest question.
- **Two courtrooms may be one too many** for a 4-player game. Keep B sealed as a
  reveal for higher player counts.
- **The staff keycard may be too strong.** If the shortcut saves 12 s per trip
  across 20 trips, that's 4 minutes — a quarter of the run. Start it at −20 %, not
  −35 %.
