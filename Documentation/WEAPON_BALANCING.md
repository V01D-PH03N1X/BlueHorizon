# Weapon Balancing

Values are the game-design specification (not real-world data). They live in
`ScriptableObjects/Weapons/*.asset`; the generator never overwrites existing weapon assets.
The specified damage values are implemented unchanged. Any future change must be recorded in the table at the end.

## Weapons

| | Assault Rifle (primary, key 1) | Pistol (secondary, key 2) |
|---|---|---|
| Head / Torso / Legs | **165 / 55 / 35** | **85 / 35 / 15** |
| Fire mode | automatic | semi-automatic |
| Fire rate | **4.5 shots/s** (0.222 s) | **1.6 shots/s** (0.625 s) |
| Magazine / reserve | **31** / 93 | **12** / 36 |
| Reload / equip | 2.3 s / 0.6 s | 1.5 s / 0.35 s |
| Max range | 200 m | 120 m |
| Range falloff | none (multiplier 1.0) | none (multiplier 1.0) |
| Spread (deg, cone half-angle) | base 0.15, moving +2.5, air +6, +0.35/shot (max 3), crouch ×0.7 | base 0.25, moving +1.6, air +4, +0.5/shot (max 2.5), crouch ×0.8 |
| Recoil | +0.55°/shot (max 5°), sway 0.6°, reset 0.4 s, recovery 14°/s | +1.2°/shot (max 4°), sway 0.3°, reset 0.5 s, recovery 12°/s |
| Fire while sprinting | no | no |

Reserve ammunition and the reserve values were not specified in the brief and are provisional (3 magazines).

### Time-to-kill against Striker (175 effective HP from full)

| Weapon | Hits to kill | Notes |
|---|---|---|
| Rifle | 2 heads; 4 torso; 5 legs; 1 head + 1 torso | 1 head from full leaves 10 HP |
| Pistol | 3 heads (2 heads + 1 torso); 5 torso; 12 legs | |

## Damage pipeline (`DamageResolver`)

1. Zone base damage from the weapon (head / torso / legs). Each shot resolves **exactly one** hitbox: the first
   valid hitbox along the server raycast stops the bullet.
2. × attachment damage multiplier (product of all attachments).
3. × range multiplier (linear from `falloffStart` to `falloffEnd`, down to `minMultiplier`; disabled by default).
4. Rounded half up, minimum 1.
5. Applied through barrier → armor → health (see GAME_DESIGN).

Hit zones: head sphere (r 0.16 m at 1.62 m), torso box, legs box; hitboxes scale with crouching. Teammates stop
bullets without taking damage (friendly fire off). Deployables (Wayblocker) take torso damage.

## Spread, recoil and cadence

- Spread direction uses a hash of the input-command sequence number, so client prediction and the server pick the
  same pellet direction. Recoil is a deterministic pattern applied to the next shot's aim and shown on the camera.
- Cadence is enforced in player simulation time with carry-over (`nextFireTime += interval` while the trigger is held),
  so 4.5 shots/s holds exactly despite 60 Hz ticks. Simulation time cannot outrun real time because the server bounds
  how many commands it processes (see TECHNICAL_ARCHITECTURE).
- Semi-automatic weapons need a trigger release between shots; clicks during the cooldown are ignored.
- An empty magazine starts a reload automatically; switching weapons cancels a reload.

## Attachments

`AttachmentDefinition` folds combat modifiers into `WeaponStats` (computed when the loadout changes) and exposes
presentation fields for the viewmodel/HUD, so new attachments don't touch the firing code.

| Attachment | Slot | Effect |
|---|---|---|
| **Suppressor** (implemented) | Muzzle | **×0.8 base damage** (stage 2), recoil ×0.9, other players hear a quiet muffled shot (35 % volume, 20 m range) and see no muzzle flash; `hidesMinimapFireIndicator` set (no minimap exists yet) |
| Underbarrel laser | Underbarrel | planned: spread multiplier + visible beam |
| Red-dot / holographic / magnified optic | Optic | planned: `aimFovMultiplier`, `reticleColor`; needs an ADS state |

Suppressed damage: rifle 132 / 44 / 28, pistol 68 / 28 / 12.

The suppressor is toggled per weapon with **T** during warmup, countdown and pre-round (server-validated);
the HUD shows "SUPPRESSED · −20% DMG".

## Balance change log

| Date | Change | Reason |
|---|---|---|
| — | none (specification values) | |
