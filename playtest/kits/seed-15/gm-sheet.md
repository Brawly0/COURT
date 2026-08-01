# GM TRUTH SHEET — THE BLACKOUT WEDDING (seed 15)

Charge: theft of the backup generator key. Defendant: Nadia.
**GUILTY: NO — true culprit: Greg the Janitor**
Crime: Storage Room at 8:30 | Clarity tier: Fractured

## Occupancy matrix (ground truth — no player ever sees this)

| Actor |8:00 | 8:30 | 9:00 | 9:30 | 10:00 |
|---|---|---|---|---|---|
| Nadia (DEF) | Kitchen | Storage | Storage | Storage | Storage |
| Greg the Janitor | Kitchen | Storage | Storage | Kitchen | Kitchen |
| Officer Dowd | Storage | Kitchen | Storage | Kitchen | Storage |
| Marisol the Secretary | Wedding | Kitchen | Storage | Storage | Storage |
| Sam the Caterer | Wedding | Parking | Wedding | Back | Back |
| Victor the Cousin | Parking | Parking | Parking | Wedding | Parking |

## Corruption ledger (what really happened + how it is caught)
- **Greg the Janitor** [InferencePromotion]: was in the Storage Room at 8:30 — could only HEAR the Kitchen, never saw it
  - counter: place Greg the Janitor at 8:30 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Greg the Janitor** [DescriptorSwap]: actually saw Officer Dowd in the Kitchen at 9:30
  - counter: Victor the Cousin's own account places them in the Wedding Hall at 9:30
- **Greg the Janitor** [DescriptorSwap]: actually saw Nadia in the Storage Room at 8:30
  - counter: Sam the Caterer truly saw Victor the Cousin in the Parking Lot at 8:30
- **Officer Dowd** [TimeShift]: actually saw Marisol the Secretary in the Kitchen at 8:30
  - counter: Nadia truly saw Marisol the Secretary in the Storage Room at 9:00
- **Officer Dowd** [InferencePromotion]: was in the Storage Room at 8:00 — could only HEAR the Kitchen, never saw it
  - counter: place Officer Dowd at 8:00 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Marisol the Secretary** [DescriptorSwap]: actually saw Nadia in the Storage Room at 9:00
  - counter: Sam the Caterer's own account places them in the Wedding Hall at 9:00
- **Marisol the Secretary** [InferencePromotion]: was in the Storage Room at 10:00 — could only HEAR the Back Office, never saw it
  - counter: place Marisol the Secretary at 10:00 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Sam the Caterer** [InferencePromotion]: was in the Back Office at 10:00 — could only HEAR the Storage Room, never saw it
  - counter: place Sam the Caterer at 10:00 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Victor the Cousin** [InferencePromotion]: was in the Wedding Hall at 9:30 — could only HEAR the Kitchen, never saw it
  - counter: place Victor the Cousin at 9:30 via others' sightings or the matrix — direct sight was impossible (hearsay)
- **Greg the Janitor** [SelfPreservation]: claims the Wedding Hall at 8:30 — was at the Storage Room (the crime); also denies seeing anyone there
  - counter: door log, CCTV, and honest witnesses at the scene

## Defendant hand fidelity
- [HAZY] You think you remember: being in the Kitchen at 8:00 -> Reliable
- [CLEAR] You remember: being in the Back Office at 8:30 -> Corrupted (spiked drink — the toxicology slip at the Lab proves it)
- [CLEAR] You remember: being in the Storage Room at 9:00 -> Reliable
- [CLEAR] You remember: being in the Storage Room at 9:30 -> Reliable
- [CLEAR] You remember: being in the Storage Room at 10:00 -> Reliable
- [CLEAR] Secret: you were hiding from the groom's mother at 8:00 and lied to police about it -> Reliable

## The proof chain (pooled-solvable facts)
- prints: Greg the Janitor handled the backup generator key
- door log: Greg the Janitor inside the Storage Room through 8:30
- CCTV: Greg the Janitor entered the Storage Room at 8:30
- witness: Nadia saw Greg the Janitor at the scene

## Evidence contents (GM eyes only)
- **the backup generator key (recovered)** (Impound cage, parking garage): recovered near the Storage Room
- **Fingerprint card: Nadia, Greg the Janitor** (Lab tray (processing: 90s)): handlers listed in touch order
- **CCTV tape: corridor Kitchen -> Storage Room (5 entries)** (Security office console): Nadia passed Kitchen -> Storage Room at 8:30; Greg the Janitor passed Kitchen -> Storage Room at 8:30; Officer Dowd passed Kitchen -> Storage Room at 9:00; Officer Dowd passed Kitchen -> Storage Room at 10:00; Marisol the Secretary passed Kitchen -> Storage Room at 9:00
- **Catering schedule (places staff by slot)** (Archives, row 3): corroborates staff members' true movements
- **Door log: Storage Room** (Archives, row 5): Officer Dowd at 8:00; Nadia at 8:30; Greg the Janitor at 8:30; Officer Dowd at 9:00; Marisol the Secretary at 9:00; Officer Dowd at 10:00
- **Toxicology slip (defendant's blood sample)** (Lab tray (processing: 120s)): sedative present — the defendant's confident memories are chemically unreliable

## Witness opening statements (read aloud on first interview)
### Greg the Janitor
- I was in the Kitchen at 8:00.
- I was in the Wedding Hall at 8:30.
- I saw Nadia in the Kitchen at 8:00.
- I saw Nadia in the Storage Room at 9:00.
- I saw Officer Dowd in the Storage Room at 9:00.
### Officer Dowd
- I was in the Storage Room at 8:00.
- I was in the Kitchen at 8:30.
- I saw Marisol the Secretary in the Kitchen at 9:00.
- I saw Nadia in the Storage Room at 9:00.
- I saw Greg the Janitor in the Storage Room at 9:00.
### Marisol the Secretary
- I was in the Wedding Hall at 8:00.
- I was in the Kitchen at 8:30.
- I saw Sam the Caterer in the Wedding Hall at 8:00.
- I saw Officer Dowd in the Kitchen at 8:30.
- I saw Sam the Caterer in the Storage Room at 9:00.
### Sam the Caterer
- I was in the Wedding Hall at 8:00.
- I was in the Parking Lot at 8:30.
- I saw Marisol the Secretary in the Wedding Hall at 8:00.
- I saw Victor the Cousin in the Parking Lot at 8:30.
- I saw Nadia in the Storage Room at 10:00.
### Victor the Cousin
- I was in the Parking Lot at 8:00.
- I was in the Parking Lot at 8:30.
- I saw Sam the Caterer in the Parking Lot at 8:30.
- I saw Greg the Janitor in the Kitchen at 9:30.

## GM rules
- The handout is each witness's OPENING statement only. They know their whole
  matrix row + everything in Obs — answer follow-up questions from this sheet,
  in character, keeping every corrupted memory corrupted. They believe it.
- Baggage facts surface when players ask the right questions or fetch the evidence.

## Baggage (the defendant looks guilty regardless — the Baggage Rule)
- Nadia's prints are on the backup generator key (they moved it at 8:00 — routine, but nobody asked)
- Nadia told police they were in the Back Office at 8:00 — actually in the Kitchen, hiding from the groom's mother (the Secret)
- A guest saw Nadia leaving in a hurry at 9:00
