# GM TRUTH SHEET — THE GOAT AFFAIR (seed 1)

Charge: theft of the prize goat. Defendant: Nadia.
**GUILTY: NO — true culprit: Officer Dowd**
Crime: Back Office at 8:30 | Clarity tier: Lucid

## Occupancy matrix (ground truth — no player ever sees this)

| Actor |8:00 | 8:30 | 9:00 | 9:30 | 10:00 |
|---|---|---|---|---|---|
| Nadia (DEF) | Wedding | Kitchen | Storage | Back | Storage |
| Greg the Janitor | Wedding | Parking | Wedding | Kitchen | Wedding |
| Officer Dowd | Wedding | Back | Back | Back | Back |
| Marisol the Secretary | Wedding | Parking | Wedding | Parking | Wedding |
| Sam the Caterer | Kitchen | Kitchen | Wedding | Parking | Wedding |
| Victor the Cousin | Parking | Wedding | Parking | Parking | Wedding |

## Corruption ledger (what really happened + how it is caught)
- **Greg the Janitor** [TimeShift]: actually saw Nadia in the Wedding Hall at 8:00
  - counter: Sam the Caterer truly saw Nadia in the Kitchen at 8:30
- **Officer Dowd** [DescriptorSwap]: actually saw Nadia in the Back Office at 9:30
  - counter: Sam the Caterer truly saw Marisol the Secretary in the Parking Lot at 9:30
- **Officer Dowd** [DescriptorSwap]: actually saw Nadia in the Wedding Hall at 8:00
  - counter: Victor the Cousin's own account places them in the Parking Lot at 8:00
- **Officer Dowd** [DescriptorSwap]: actually saw Marisol the Secretary in the Wedding Hall at 8:00
  - counter: Sam the Caterer's own account places them in the Kitchen at 8:00
- **Marisol the Secretary** [TimeShift]: actually saw Greg the Janitor in the Parking Lot at 8:30
  - counter: Sam the Caterer truly saw Greg the Janitor in the Wedding Hall at 9:00
- **Marisol the Secretary** [TimeShift]: actually saw Officer Dowd in the Wedding Hall at 8:00
  - counter: Officer Dowd's own account places them in the Back Office at 8:30
- **Sam the Caterer** [DescriptorSwap]: actually saw Nadia in the Kitchen at 8:30
  - counter: Greg the Janitor truly saw Marisol the Secretary in the Parking Lot at 8:30
- **Sam the Caterer** [TimeShift]: actually saw Victor the Cousin in the Wedding Hall at 10:00
  - counter: Marisol the Secretary truly saw Victor the Cousin in the Parking Lot at 9:30
- **Victor the Cousin** [TimeShift]: actually saw Sam the Caterer in the Parking Lot at 9:30
  - counter: Greg the Janitor truly saw Sam the Caterer in the Wedding Hall at 10:00
- **Victor the Cousin** [DescriptorSwap]: actually saw Marisol the Secretary in the Parking Lot at 9:30
  - counter: Greg the Janitor's own account places them in the Kitchen at 9:30
- **Victor the Cousin** [DescriptorSwap]: actually saw Sam the Caterer in the Wedding Hall at 10:00
  - counter: Officer Dowd's own account places them in the Back Office at 10:00
- **Officer Dowd** [SelfPreservation]: claims the Storage Room at 8:30 — was at the Back Office (the crime); also denies seeing anyone there
  - counter: door log, CCTV, and honest witnesses at the scene

## Defendant hand fidelity
- [CLEAR] You remember: being in the Wedding Hall at 8:00 -> Reliable
- [HAZY] You think you remember: being in the Kitchen at 8:30 -> Reliable
- [CLEAR] You remember: being in the Storage Room at 9:00 -> Reliable
- [CLEAR] You remember: being in the Back Office at 9:30 -> Reliable
- [HAZY] You think you remember: being in the Storage Room at 10:00 -> Reliable
- [CLEAR] You remember: NOT taking the prize goat at 8:30 -> Reliable
- [CLEAR] Secret: you were eating cake straight from the tray at 9:00 and lied to police about it -> Reliable

## The proof chain (pooled-solvable facts)
- prints: Officer Dowd handled the prize goat
- door log: Officer Dowd inside the Back Office through 8:30
- CCTV: Officer Dowd entered the Back Office at 8:30

## Evidence contents (GM eyes only)
- **the prize goat (recovered)** (Impound cage, parking garage): recovered near the Back Office
- **Fingerprint card: Nadia, Officer Dowd** (Lab tray (processing: 90s)): handlers listed in touch order
- **CCTV tape: corridor Wedding Hall -> Back Office (1 entries)** (Security office console): Officer Dowd passed Wedding Hall -> Back Office at 8:30
- **Catering schedule (places staff by slot)** (Archives, row 3): corroborates staff members' true movements
- **Door log: Back Office** (Archives, row 5): Officer Dowd at 8:30; Nadia at 9:30

## Witness opening statements (read aloud on first interview)
### Greg the Janitor
- I was in the Wedding Hall at 8:00.
- I was in the Parking Lot at 8:30.
- I saw Nadia in the Wedding Hall at 8:30.
- I saw Officer Dowd in the Wedding Hall at 8:00.
- I saw Marisol the Secretary in the Wedding Hall at 8:00.
### Officer Dowd
- I was in the Wedding Hall at 8:00.
- I was in the Storage Room at 8:30.
- I saw Victor the Cousin in the Wedding Hall at 8:00.
- I saw Greg the Janitor in the Wedding Hall at 8:00.
- I saw Sam the Caterer in the Wedding Hall at 8:00.
### Marisol the Secretary
- I was in the Wedding Hall at 8:00.
- I was in the Parking Lot at 8:30.
- I saw Officer Dowd in the Wedding Hall at 8:30.
- I saw Nadia in the Wedding Hall at 8:00.
- I saw Greg the Janitor in the Wedding Hall at 8:00.
### Sam the Caterer
- I was in the Kitchen at 8:00.
- I was in the Kitchen at 8:30.
- I saw Marisol the Secretary in the Kitchen at 8:30.
- I saw Greg the Janitor in the Wedding Hall at 9:00.
- I saw Marisol the Secretary in the Wedding Hall at 9:00.
### Victor the Cousin
- I was in the Parking Lot at 8:00.
- I was in the Wedding Hall at 8:30.
- I saw Greg the Janitor in the Parking Lot at 9:30.
- I saw Sam the Caterer in the Parking Lot at 10:00.
- I saw Greg the Janitor in the Wedding Hall at 10:00.

## GM rules
- The handout is each witness's OPENING statement only. They know their whole
  matrix row + everything in Obs — answer follow-up questions from this sheet,
  in character, keeping every corrupted memory corrupted. They believe it.
- Baggage facts surface when players ask the right questions or fetch the evidence.

## Baggage (the defendant looks guilty regardless — the Baggage Rule)
- Nadia's prints are on the prize goat (they moved it at 8:00 — routine, but nobody asked)
- Nadia told police they were in the Kitchen at 9:00 — actually in the Storage Room, eating cake straight from the tray (the Secret)
- A guest saw Nadia leaving in a hurry at 9:00
