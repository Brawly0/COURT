# CASE CLOSED

4–8 player courtroom-detective game. Procedurally generated crimes, 15-minute investigation,
8-minute trial before a deterministic AI judge, and a truth reveal nobody saw coming.

**Design canon lives in the Obsidian vault:** `Desktop/obsidian/02 - Projects/Case Closed/`
(15-part GDD + COURT merge notes + Phase 0 balance report). This repo is the game itself.

## Layout

```
src/CaseClosed.TruthEngine/   The core: case generation, invariants, judge math.
                              Pure C#, netstandard2.1, zero dependencies — the compiled
                              DLL drops straight into Unity 6 (GDD 12).
src/CaseClosed.Console/       CLI over the engine (generate / kit / scan / validate).
tests/CaseClosed.Tests/       xunit: determinism, every generator invariant, judge math.
playtest/                     Gate 0.5 paper-playtest package: RUNBOOK, results form,
                              pre-generated kits (GM sheet + per-team handouts).
```

## Commands

```bash
dotnet test                                                # 18 tests: invariants + determinism
dotnet run --project src/CaseClosed.Console -- validate 3000
dotnet run --project src/CaseClosed.Console -- scan 1 24
dotnet run --project src/CaseClosed.Console -- generate 2
dotnet run --project src/CaseClosed.Console -- kit 2 playtest/kits/seed-2
```

## Engine guarantees (enforced in code + tests, not in hope)

- **Deterministic:** same seed → same case, any platform (own PCG32; never System.Random).
- **Pooled-solvable:** ≥3 independent facts implicate the true culprit, or the case rerolls.
- **Detectability:** every false memory has a discoverable counter, or it is reverted.
- **The Baggage Rule:** the defendant always looks guilty three innocent ways.
- **Stamp contract:** CLEAR memories lie only in Fractured cases, and the cause is findable.
- **The defendant is never scripted to lie** — their lies are the player's choice (Clarity system).

## Roadmap position

Phase 1 (Truth Engine console) ✅ · Phase 2 (paper playtest package) ✅ — the playtest itself
needs humans; see `playtest/RUNBOOK.md`. Next: Gate 0.5 sessions → Unity 6 netcode spine
(FishNet + Steam relay, graybox courthouse) → Gate 1 reveal test.
