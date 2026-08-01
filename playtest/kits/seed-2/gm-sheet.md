# GM TRUTH SHEET — THE HUMMUS HEIST (seed 2)

Charge: theft of the hummus vat. Defendant: Nadia.
**GUILTY: YES — Nadia**
Crime: Wedding Hall at 9:30 | Clarity tier: Lucid

## Occupancy matrix (ground truth — no player ever sees this)

| Actor |8:00 | 8:30 | 9:00 | 9:30 | 10:00 |
|---|---|---|---|---|---|
| Nadia (DEF) | Back | Back | Back | Wedding | Kitchen |
| Greg the Janitor | Wedding | Wedding | Parking | Wedding | Back |
| Officer Dowd | Storage | Back | Back | Storage | Kitchen |
| Marisol the Secretary | Wedding | Wedding | Wedding | Back | Storage |
| Sam the Caterer | Storage | Back | Storage | Storage | Storage |
| Victor the Cousin | Wedding | Back | Back | Wedding | Kitchen |

## Corruption ledger (what really happened + how it is caught)
- **Greg the Janitor** [DescriptorSwap]: actually saw Victor the Cousin in the Wedding Hall at 8:00
  - counter: Sam the Caterer truly saw Officer Dowd in the Storage Room at 8:00
- **Greg the Janitor** [TimeShift]: actually saw Victor the Cousin in the Wedding Hall at 9:30
  - counter: Nadia truly saw Victor the Cousin in the Kitchen at 10:00
- **Officer Dowd** [TimeShift]: actually saw Sam the Caterer in the Back Office at 8:30
  - counter: Sam the Caterer's own account places them in the Storage Room at 9:00
- **Officer Dowd** [DescriptorSwap]: actually saw Sam the Caterer in the Storage Room at 8:00
  - counter: Nadia's own account places them in the Back Office at 8:00
- **Marisol the Secretary** [InferencePromotion]: was in the Storage Room at 10:00 — could only HEAR the Kitchen, never saw it
  - counter: place Marisol the Secretary at 10:00 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Marisol the Secretary** [DescriptorSwap]: actually saw Greg the Janitor in the Wedding Hall at 8:00
  - counter: Sam the Caterer truly saw Officer Dowd in the Storage Room at 8:00
- **Marisol the Secretary** [InferencePromotion]: was in the Back Office at 9:30 — could only HEAR the Wedding Hall, never saw it
  - counter: place Marisol the Secretary at 9:30 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Sam the Caterer** [InferencePromotion]: was in the Storage Room at 9:30 — could only HEAR the Back Office, never saw it
  - counter: place Sam the Caterer at 9:30 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Sam the Caterer** [DescriptorSwap]: actually saw Nadia in the Back Office at 8:30
  - counter: Greg the Janitor truly saw Marisol the Secretary in the Wedding Hall at 8:30
- **Sam the Caterer** [DescriptorSwap]: actually saw Marisol the Secretary in the Storage Room at 10:00
  - counter: Officer Dowd truly saw Nadia in the Kitchen at 10:00
- **Victor the Cousin** [InferencePromotion]: was in the Kitchen at 10:00 — could only HEAR the Storage Room, never saw it
  - counter: place Victor the Cousin at 10:00 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Victor the Cousin** [ProtectAgenda]: deleted their sighting of Nadia at the Wedding Hall at 9:30 (protecting them)
  - counter: cross-reference: door log, CCTV, and other witnesses at the scene

## Defendant hand fidelity
- [HAZY] You think you remember: being in the Back Office at 8:00 -> Reliable
- [CLEAR] You remember: being in the Back Office at 8:30 -> Reliable
- [CLEAR] You remember: being in the Back Office at 9:00 -> Reliable
- [HAZY] You think you remember: being in the Wedding Hall at 9:30 -> Reliable
- [CLEAR] You remember: being in the Kitchen at 10:00 -> Reliable
- [CLEAR] You remember: taking the hummus vat at 9:30 -> Reliable
- [CLEAR] Secret: you were eating cake straight from the tray at 9:00 and lied to police about it -> Reliable

## The proof chain (pooled-solvable facts)
- prints: Nadia handled the hummus vat
- door log: Nadia inside the Wedding Hall through 9:30
- CCTV: Nadia entered the Wedding Hall at 9:30
- witness: Greg the Janitor saw Nadia at the scene

## Evidence contents (GM eyes only)
- **the hummus vat (recovered)** (Impound cage, parking garage): recovered near the Wedding Hall
- **Fingerprint card: Nadia** (Lab tray (processing: 90s)): handlers listed in touch order
- **CCTV tape: corridor Back Office -> Wedding Hall (2 entries)** (Security office console): Nadia passed Back Office -> Wedding Hall at 9:30; Victor the Cousin passed Back Office -> Wedding Hall at 9:30
- **Catering schedule (places staff by slot)** (Archives, row 3): corroborates staff members' true movements
- **Door log: Wedding Hall** (Archives, row 5): Greg the Janitor at 8:00; Marisol the Secretary at 8:00; Victor the Cousin at 8:00; Nadia at 9:30; Greg the Janitor at 9:30; Victor the Cousin at 9:30

## Witness opening statements (read aloud on first interview)
### Greg the Janitor
- I was in the Wedding Hall at 8:00.
- I was in the Wedding Hall at 9:30.
- I saw Nadia in the Wedding Hall at 9:30.
- I saw Marisol the Secretary in the Wedding Hall at 8:00.
- I saw Officer Dowd in the Wedding Hall at 8:00.
### Officer Dowd
- I was in the Storage Room at 8:00.
- I was in the Storage Room at 9:30.
- I saw Nadia in the Storage Room at 8:00.
- I saw Nadia in the Back Office at 8:30.
- I saw Sam the Caterer in the Back Office at 9:00.
### Marisol the Secretary
- I was in the Wedding Hall at 8:00.
- I was in the Back Office at 9:30.
- I saw Nadia in the Wedding Hall at 9:30.
- I saw Officer Dowd in the Wedding Hall at 8:00.
- I saw Victor the Cousin in the Wedding Hall at 8:00.
### Sam the Caterer
- I was in the Storage Room at 8:00.
- I was in the Storage Room at 9:30.
- I saw Officer Dowd in the Storage Room at 8:00.
- I saw Marisol the Secretary in the Back Office at 8:30.
- I saw Officer Dowd in the Back Office at 8:30.
### Victor the Cousin
- I was in the Wedding Hall at 8:00.
- I was in the Wedding Hall at 9:30.
- I saw Greg the Janitor in the Wedding Hall at 9:30.
- I saw Greg the Janitor in the Wedding Hall at 8:00.
- I saw Marisol the Secretary in the Wedding Hall at 8:00.

## GM rules
- The handout is each witness's OPENING statement only. They know their whole
  matrix row + everything in Obs — answer follow-up questions from this sheet,
  in character, keeping every corrupted memory corrupted. They believe it.
- Baggage facts surface when players ask the right questions or fetch the evidence.

## Baggage (the defendant looks guilty regardless — the Baggage Rule)
- Nadia's prints are on the hummus vat (they moved it at 9:00 — routine, but nobody asked)
- Nadia told police they were in the Wedding Hall at 9:00 — actually in the Back Office, eating cake straight from the tray (the Secret)
- A guest saw Nadia leaving in a hurry at 10:00
