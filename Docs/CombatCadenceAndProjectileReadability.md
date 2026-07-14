# Combat cadence and projectile readability

## Canonical ordinary-hit measurement

The survivability target is measured from the full-health production roster at
level 1 with `statMultiplier = 1`, no progression, cards, or equipment. Count
repeated unmodified direct-damage components from an ordinary basic attack. Do
not use Supers or Ward Step; do not heal or regenerate between hits; and do not
apply invulnerability, cover, or friendly fire.

The canonical matrix intentionally excludes conditional specialization value:
Fire burn and burning ground, Storm chain damage, and all status, damage-over-
time, secondary, or area effects. Frost slow deals no damage. Those mechanics
remain live and can reward favorable combat conditions without redefining the
defensible direct-hit baseline.

| Attacker ↓ / defender → | Cinder 96 HP | Rime 112 HP | Tempest 88 HP | Thorn 96 HP |
|---|---:|---:|---:|---:|
| Cinder, 27 damage | 4 | 5 | 4 | 4 |
| Rime, 23 damage | 5 | 5 | 4 | 5 |
| Tempest, 26 damage | 4 | 5 | 4 | 4 |
| Thorn, 30 damage | 4 | 4 | 3 | 4 |

Every pairing is in the intended 3–5 ordinary direct-hit KO band. The tuning
retains roster shape: Rime is the toughest and lowest direct-damage controller;
Tempest is the fragile, fast-cadence skirmisher; Thorn has the slowest, highest
precision hit and longest lane; Cinder remains middle-weight artillery whose
conditional burn is outside the canonical matrix.

Boundary checks additionally protect the live mechanics: two Cinder ordinary
hits remain non-lethal against the minimum healthy roster HP even under the
conservative upper bound of direct damage plus full burn and burning-ground
fractions, and no roster Super direct hit can one-shot that minimum HP. Cinder's
Super also remains below that threshold when the same conservative Fire
fractions are included.

## Readability contract

Projectile visuals are Brawl-owned presentation layered over the existing
authoritative projectile:

- a fixed team-colored halo and trail communicate the source team;
- a distinct roster threat glyph/accent communicates burn, control, chain, or
  precision danger;
- a white capped crossbar communicates the authoritative `StopsOnWorld` travel
  rule while the shot is in flight (the enum also reserves a pass-through cue);
- a gold outer halo distinguishes a Super from a basic projectile;
- the horizontal splash warning uses the exact launch `blastRadius` and is
  absent when the authoritative radius is zero;
- direct hit, world obstruction, and range expiry emit distinct short pooled
  outcome marks after the authoritative sweep resolves.

The generated renderers use a dedicated Brawl runtime material and property
blocks. They never modify a projectile prefab's imported renderer or shared
vendor material. The projectile lease resets team, threat, tier, radius,
property blocks, generated renderer visibility, and trail history; the impact
lease resets outcome state before returning to `CombatObjectPool`. Outcome cues
contain no collider, damage, or targeting behavior and cannot alter combat.
