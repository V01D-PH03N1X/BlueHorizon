using System.IO;
using BlueHorizon.Abilities;
using BlueHorizon.Agents;
using BlueHorizon.Attachments;
using BlueHorizon.Combat;
using BlueHorizon.Core;
using BlueHorizon.GameModes;
using BlueHorizon.Match;
using BlueHorizon.Player;
using BlueHorizon.UI;
using BlueHorizon.Weapons;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace BlueHorizon.EditorTools
{
    /// <summary>Creates materials, data assets, network prefabs and UI settings for the vertical slice.</summary>
    public static class ContentFactory
    {
        public const string ContentPath = ProjectSetup.Root + "/Settings/GameContent.asset";
        const string Materials = ProjectSetup.Root + "/Materials";
        const string Data = ProjectSetup.Root + "/ScriptableObjects";
        const string Prefabs = ProjectSetup.Root + "/Prefabs";
        public const string PanelSettingsPath = ProjectSetup.Root + "/UI/PanelSettings.asset";
        public const string ThemePath = ProjectSetup.Root + "/UI/BlueHorizonTheme.tss";
        public const string StyleSheetPath = ProjectSetup.Root + "/UI/BlueHorizon.uss";
        public const string NetworkPrefabsPath = ProjectSetup.Root + "/Settings/NetworkPrefabs.asset";

        public static GameContent CreateOrUpdateContent()
        {
            EnsureFolder(Materials);
            EnsureFolder(Data + "/Weapons");
            EnsureFolder(Data + "/Attachments");
            EnsureFolder(Data + "/Agents");
            EnsureFolder(Data + "/Abilities");
            EnsureFolder(Data + "/GameModes");
            EnsureFolder(Prefabs + "/Players");
            EnsureFolder(Prefabs + "/Abilities");
            EnsureFolder(Prefabs + "/Match");
            EnsureFolder(ProjectSetup.Root + "/Settings");
            EnsureFolder(ProjectSetup.Root + "/UI");

            GameContent content = LoadOrCreate<GameContent>(ContentPath, out _);

            content.litMaterial = CreateLit("M_Lit", new Color(0.7f, 0.72f, 0.75f));
            content.emissiveMaterial = CreateEmissive("M_Emissive", new Color(0.3f, 0.7f, 1f));
            content.transparentMaterial = CreateTransparent("M_Transparent", new Color(0.8f, 0.8f, 0.82f, 0.6f));
            content.unlitMaterial = CreateUnlit("M_Unlit", Color.white);

            WeaponDefinition rifle = LoadOrCreate<WeaponDefinition>(Data + "/Weapons/AssaultRifle.asset", out bool newRifle);
            if (newRifle) ConfigureRifle(rifle);
            WeaponDefinition pistol = LoadOrCreate<WeaponDefinition>(Data + "/Weapons/Pistol.asset", out bool newPistol);
            if (newPistol) ConfigurePistol(pistol);
            AttachmentDefinition suppressor = LoadOrCreate<AttachmentDefinition>(Data + "/Attachments/Suppressor.asset", out bool newSup);
            if (newSup) ConfigureSuppressor(suppressor);

            // Milestone 4 weapons and attachments.
            WeaponDefinition revolver = LoadOrCreate<WeaponDefinition>(Data + "/Weapons/Revolver.asset", out bool newRevolver);
            if (newRevolver) ConfigureRevolver(revolver);
            WeaponDefinition shotgun = LoadOrCreate<WeaponDefinition>(Data + "/Weapons/Shotgun.asset", out bool newShotgun);
            if (newShotgun) ConfigureShotgun(shotgun);
            WeaponDefinition dmr = LoadOrCreate<WeaponDefinition>(Data + "/Weapons/PrecisionRifle.asset", out bool newDmr);
            if (newDmr) ConfigurePrecisionRifle(dmr);
            WeaponDefinition sniper = LoadOrCreate<WeaponDefinition>(Data + "/Weapons/SniperRifle.asset", out bool newSniper);
            if (newSniper) ConfigureSniper(sniper);

            AttachmentDefinition laser = LoadOrCreate<AttachmentDefinition>(Data + "/Attachments/Laser.asset", out bool newLaser);
            if (newLaser) ConfigureLaser(laser);
            AttachmentDefinition redDot = LoadOrCreate<AttachmentDefinition>(Data + "/Attachments/RedDot.asset", out bool newRedDot);
            if (newRedDot) ConfigureOptic(redDot, "red_dot", "Red Dot Sight", ReticleStyle.Dot, fov: 0.85f, adsTime: 0.95f, adsSpread: 1f,
                magnified: false, reticle: new Color(1f, 0.15f, 0.12f), size: new Vector3(0.035f, 0.04f, 0.06f));
            AttachmentDefinition holo = LoadOrCreate<AttachmentDefinition>(Data + "/Attachments/Holographic.asset", out bool newHolo);
            if (newHolo) ConfigureOptic(holo, "holo", "Holographic Sight", ReticleStyle.Holographic, fov: 0.8f, adsTime: 1.05f, adsSpread: 0.9f,
                magnified: false, reticle: new Color(1f, 0.25f, 0.2f), size: new Vector3(0.05f, 0.05f, 0.08f));
            AttachmentDefinition scope4 = LoadOrCreate<AttachmentDefinition>(Data + "/Attachments/Scope4x.asset", out bool newScope4);
            if (newScope4) ConfigureOptic(scope4, "scope_4x", "4x Scope", ReticleStyle.Scope, fov: 0.27f, adsTime: 1.6f, adsSpread: 0.6f,
                magnified: true, reticle: new Color(0.05f, 0.05f, 0.05f), size: new Vector3(0.045f, 0.045f, 0.2f));
            AttachmentDefinition scope8 = LoadOrCreate<AttachmentDefinition>(Data + "/Attachments/Scope8x.asset", out bool newScope8);
            if (newScope8) ConfigureOptic(scope8, "scope_8x", "8x Scope", ReticleStyle.Scope, fov: 0.14f, adsTime: 1f, adsSpread: 1f,
                magnified: true, reticle: new Color(0.05f, 0.05f, 0.05f), size: new Vector3(0.05f, 0.05f, 0.26f));

            // Categories, compatibility and the sniper's built-in scope are always enforced (they are rules, not tuning).
            SetCompatibility(rifle, WeaponCategory.AssaultRifle, null, suppressor, laser, redDot, holo, scope4);
            SetCompatibility(pistol, WeaponCategory.Pistol, null, suppressor, laser, redDot);
            SetCompatibility(revolver, WeaponCategory.Revolver, null, laser, redDot);
            SetCompatibility(shotgun, WeaponCategory.Shotgun, null, laser, redDot, holo);
            SetCompatibility(dmr, WeaponCategory.PrecisionRifle, null, suppressor, laser, redDot, holo, scope4);
            SetCompatibility(sniper, WeaponCategory.SniperRifle, scope8, suppressor, laser);

            SmokeGrenadeDefinition smoke = LoadOrCreate<SmokeGrenadeDefinition>(Data + "/Abilities/SmokeGrenade.asset", out bool newSmoke);
            if (newSmoke) ConfigureSmoke(smoke);
            WayblockerDefinition wall = LoadOrCreate<WayblockerDefinition>(Data + "/Abilities/Wayblocker.asset", out bool newWall);
            if (newWall) ConfigureWayblocker(wall);
            HealAbilityDefinition heal = LoadOrCreate<HealAbilityDefinition>(Data + "/Abilities/Heal.asset", out bool newHeal);
            if (newHeal) ConfigureHeal(heal);

            AgentDefinition striker = LoadOrCreate<AgentDefinition>(Data + "/Agents/Striker.asset", out bool newAgent);
            if (newAgent) ConfigureStriker(striker);
            striker.utility1 = smoke;
            striker.utility2 = wall;
            striker.signature = heal;
            EditorUtility.SetDirty(striker);

            HeavyBombSettings heavyBomb = LoadOrCreate<HeavyBombSettings>(Data + "/GameModes/HeavyBomb.asset", out bool newMode);
            if (newMode)
            {
                heavyBomb.modeId = "heavybomb";
                heavyBomb.displayName = "HeavyBomb";
                EditorUtility.SetDirty(heavyBomb);
            }

            if (string.IsNullOrEmpty(heavyBomb.description))
            {
                heavyBomb.description = "Attackers plant the bomb at site A or B; defenders stop them or defuse it.";
                EditorUtility.SetDirty(heavyBomb);
            }

            var duel = LoadOrCreate<DuelSettings>(Data + "/GameModes/Duel.asset", out bool newDuel);
            if (newDuel) Configure(duel, "duel", "Duel", "1v1 elimination rounds. A timeout goes to whoever has more health left.",
                teamSize: 1, maxPlayers: 2, minPlayers: 1, timeLimit: 60f, preRound: 3f, postRound: 4f, respawn: 0f, scoreLimit: 5, ffa: false);
            var deathmatch = LoadOrCreate<DeathmatchSettings>(Data + "/GameModes/Deathmatch.asset", out bool newDm);
            if (newDm) Configure(deathmatch, "deathmatch", "Deathmatch", "Free-for-all. First to the kill limit wins.",
                teamSize: 15, maxPlayers: 15, minPlayers: 2, timeLimit: 600f, preRound: 3f, postRound: 0f, respawn: 3f, scoreLimit: 25, ffa: true);
            var capture = LoadOrCreate<CaptureAreaSettings>(Data + "/GameModes/CaptureArea.asset", out bool newCapture);
            if (newCapture) Configure(capture, "capture", "Capture the Area", "Take the central area and hold it. The holding team scores every second.",
                teamSize: 5, maxPlayers: 10, minPlayers: 1, timeLimit: 480f, preRound: 5f, postRound: 0f, respawn: 5f, scoreLimit: 100, ffa: false);
            var weaponGame = LoadOrCreate<WeaponGameSettings>(Data + "/GameModes/WeaponGame.asset", out bool newWg);
            if (newWg)
            {
                Configure(weaponGame, "weapongame", "Weapon Game", "Every kill gives you the next weapon. Win with a kill using the last one.",
                    teamSize: 12, maxPlayers: 12, minPlayers: 2, timeLimit: 600f, preRound: 3f, postRound: 0f, respawn: 2f, scoreLimit: 4, ffa: true);
                weaponGame.abilitiesEnabled = false;
                weaponGame.loadoutChangesEnabled = false;
                EditorUtility.SetDirty(weaponGame);
            }
            if (weaponGame.ladder == null || weaponGame.ladder.Length <= 4)
            {
                // Indices into content.weapons below: 0 rifle, 1 pistol, 2 revolver, 3 shotgun, 4 precision rifle, 5 sniper.
                weaponGame.ladder = new[]
                {
                    new WeaponGameLevel { weaponIndex = 0 },
                    new WeaponGameLevel { weaponIndex = 3 },
                    new WeaponGameLevel { weaponIndex = 4 },
                    new WeaponGameLevel { weaponIndex = 5 },
                    new WeaponGameLevel { weaponIndex = 0, suppressed = true },
                    new WeaponGameLevel { weaponIndex = 2 },
                    new WeaponGameLevel { weaponIndex = 1 },
                    new WeaponGameLevel { weaponIndex = 1, suppressed = true },
                };
                EditorUtility.SetDirty(weaponGame);
            }

            content.heavyBomb = heavyBomb;
            content.modes = new GameModeDefinition[] { heavyBomb, duel, deathmatch, capture, weaponGame };
            content.defaultAgent = striker;
            content.primaryWeapon = rifle;
            content.secondaryWeapon = pistol;
            content.suppressor = suppressor;
            // Order matters: loadouts and the Weapon Game ladder store indices. Append only.
            content.weapons = new[] { rifle, pistol, revolver, shotgun, dmr, sniper };
            content.attachments = new[] { suppressor, laser, redDot, holo, scope4, scope8 };

            content.playerPrefab = CreatePlayerPrefab();
            content.matchManagerPrefab = CreateSimplePrefab<MatchManager>(Prefabs + "/Match/MatchManager.prefab", GameLayers.Default);
            content.smokeGrenadePrefab = CreateSimplePrefab<SmokeGrenadeProjectile>(Prefabs + "/Abilities/SmokeGrenade.prefab", GameLayers.Projectile);
            content.smokeCloudPrefab = CreateSimplePrefab<SmokeCloud>(Prefabs + "/Abilities/SmokeCloud.prefab", GameLayers.IgnoreRaycast);
            content.wayblockerPrefab = CreateWayblockerPrefab();
            smoke.hudColor = new Color(0.75f, 0.8f, 0.85f);

            CreateNetworkPrefabsList(content);
            CreatePanelSettings();

            EditorUtility.SetDirty(content);
            AssetDatabase.SaveAssets();
            return content;
        }

        // ───────────── Data ─────────────

        static void Configure(GameModeDefinition m, string id, string name, string description, int teamSize, int maxPlayers, int minPlayers,
            float timeLimit, float preRound, float postRound, float respawn, int scoreLimit, bool ffa)
        {
            m.modeId = id;
            m.displayName = name;
            m.description = description;
            m.teamSize = teamSize;
            m.maxPlayers = maxPlayers;
            m.minPlayersPerTeam = minPlayers;
            m.timeLimit = timeLimit;
            m.preRoundTime = preRound;
            m.postRoundTime = postRound;
            m.respawnDelay = respawn;
            m.scoreLimit = scoreLimit;
            m.freeForAll = ffa;
            m.matchStartCountdown = 5f;
            m.matchOverTime = 12f;
            EditorUtility.SetDirty(m);
        }

        static void ConfigureRifle(WeaponDefinition w)
        {
            w.weaponId = "assault_rifle";
            w.displayName = "Assault Rifle";
            w.slot = WeaponSlot.Primary;
            w.fireMode = FireMode.Automatic;
            w.fireRate = 4.5f;
            w.magazineSize = 31;
            w.reserveAmmo = 93;
            w.reloadTime = 2.3f;
            w.equipTime = 0.6f;
            w.damage = new ZoneDamage { head = 165, torso = 55, legs = 35 };
            w.falloff = new RangeFalloff { falloffStart = 1000f, falloffEnd = 1000f, minMultiplier = 1f };
            w.maxRange = 200f;
            w.spread = new SpreadSettings { baseSpread = 0.15f, movingSpread = 2.5f, airborneSpread = 6f, bloomPerShot = 0.35f, maxBloom = 3f, crouchMultiplier = 0.7f };
            w.recoil = new RecoilSettings { verticalPerShot = 0.55f, maxVertical = 5f, horizontalAmplitude = 0.6f, resetTime = 0.4f, recoveryRate = 14f };
            w.viewmodelColor = new Color(0.16f, 0.18f, 0.2f);
            w.viewmodelSize = new Vector3(0.06f, 0.1f, 0.45f);
            EditorUtility.SetDirty(w);
        }

        static void ConfigurePistol(WeaponDefinition w)
        {
            w.weaponId = "pistol";
            w.displayName = "Pistol";
            w.slot = WeaponSlot.Secondary;
            w.fireMode = FireMode.SemiAutomatic;
            w.fireRate = 1.6f;
            w.magazineSize = 12;
            w.reserveAmmo = 36;
            w.reloadTime = 1.5f;
            w.equipTime = 0.35f;
            w.damage = new ZoneDamage { head = 85, torso = 35, legs = 15 };
            w.falloff = new RangeFalloff { falloffStart = 1000f, falloffEnd = 1000f, minMultiplier = 1f };
            w.maxRange = 120f;
            w.spread = new SpreadSettings { baseSpread = 0.25f, movingSpread = 1.6f, airborneSpread = 4f, bloomPerShot = 0.5f, maxBloom = 2.5f, crouchMultiplier = 0.8f };
            w.recoil = new RecoilSettings { verticalPerShot = 1.2f, maxVertical = 4f, horizontalAmplitude = 0.3f, resetTime = 0.5f, recoveryRate = 12f };
            w.viewmodelColor = new Color(0.2f, 0.2f, 0.22f);
            w.viewmodelSize = new Vector3(0.05f, 0.12f, 0.22f);
            EditorUtility.SetDirty(w);
        }

        static void ConfigureSuppressor(AttachmentDefinition a)
        {
            a.attachmentId = "suppressor";
            a.displayName = "Suppressor";
            a.slot = AttachmentSlot.Muzzle;
            a.damageMultiplier = 0.8f;
            a.spreadMultiplier = 1f;
            a.recoilMultiplier = 0.9f;
            a.suppressesFireSound = true;
            a.hidesMinimapFireIndicator = true;
            a.remoteFireVolumeMultiplier = 0.35f;
            EditorUtility.SetDirty(a);
        }

        static void SetCompatibility(WeaponDefinition w, WeaponCategory category, AttachmentDefinition fixedOptic, params AttachmentDefinition[] allowed)
        {
            w.category = category;
            w.fixedOptic = fixedOptic;
            w.allowedAttachments = allowed;
            EditorUtility.SetDirty(w);
        }

        // Milestone 4 values are a first balance proposal (see Documentation/WEAPON_BALANCING.md), not playtested.

        static void ConfigureRevolver(WeaponDefinition w)
        {
            w.weaponId = "revolver";
            w.displayName = "Revolver";
            w.slot = WeaponSlot.Secondary;
            w.fireMode = FireMode.SemiAutomatic;
            w.fireRate = 1.1f;
            w.magazineSize = 6;
            w.reserveAmmo = 24;
            w.reloadTime = 2.6f;
            w.equipTime = 0.45f;
            w.damage = new ZoneDamage { head = 140, torso = 60, legs = 40 };
            w.falloff = new RangeFalloff { falloffStart = 30f, falloffEnd = 60f, minMultiplier = 0.7f };
            w.maxRange = 120f;
            w.spread = new SpreadSettings { baseSpread = 0.3f, movingSpread = 2f, airborneSpread = 5f, bloomPerShot = 0.8f, maxBloom = 2.4f, crouchMultiplier = 0.8f };
            w.recoil = new RecoilSettings { verticalPerShot = 2.6f, maxVertical = 6f, horizontalAmplitude = 0.4f, resetTime = 0.9f, recoveryRate = 9f };
            w.adsTime = 0.2f;
            w.adsSpreadMultiplier = 0.5f;
            w.adsMoveSpeedMultiplier = 0.8f;
            w.adsFovMultiplier = 0.9f;
            w.viewmodelColor = new Color(0.32f, 0.3f, 0.28f);
            w.viewmodelSize = new Vector3(0.055f, 0.12f, 0.27f);
            w.fireSoundPitch = 0.75f;
            w.fireSoundVolume = 1.15f;
            EditorUtility.SetDirty(w);
        }

        static void ConfigureShotgun(WeaponDefinition w)
        {
            w.weaponId = "shotgun";
            w.displayName = "Shotgun";
            w.slot = WeaponSlot.Primary;
            w.fireMode = FireMode.SemiAutomatic;
            w.fireRate = 1.1f;
            w.magazineSize = 6;
            w.reserveAmmo = 24;
            w.reloadTime = 2.8f;
            w.equipTime = 0.6f;
            w.damage = new ZoneDamage { head = 30, torso = 20, legs = 12 };
            w.falloff = new RangeFalloff { falloffStart = 8f, falloffEnd = 25f, minMultiplier = 0.25f };
            w.maxRange = 60f;
            w.pellets = 8;
            w.pelletSpread = 4.5f;
            w.spread = new SpreadSettings { baseSpread = 0.5f, movingSpread = 1f, airborneSpread = 3f, bloomPerShot = 0f, maxBloom = 0f, crouchMultiplier = 1f };
            w.recoil = new RecoilSettings { verticalPerShot = 3.5f, maxVertical = 7f, horizontalAmplitude = 0.5f, resetTime = 0.8f, recoveryRate = 10f };
            w.blockFireWhileSprinting = true;
            w.adsTime = 0.22f;
            w.adsSpreadMultiplier = 0.8f;
            w.adsMoveSpeedMultiplier = 0.8f;
            w.adsFovMultiplier = 0.92f;
            w.viewmodelColor = new Color(0.24f, 0.2f, 0.16f);
            w.viewmodelSize = new Vector3(0.07f, 0.1f, 0.5f);
            w.fireSoundPitch = 0.6f;
            w.fireSoundVolume = 1.3f;
            EditorUtility.SetDirty(w);
        }

        static void ConfigurePrecisionRifle(WeaponDefinition w)
        {
            w.weaponId = "precision_rifle";
            w.displayName = "Precision Rifle";
            w.slot = WeaponSlot.Primary;
            w.fireMode = FireMode.SemiAutomatic;
            w.fireRate = 2.5f;
            w.magazineSize = 12;
            w.reserveAmmo = 36;
            w.reloadTime = 2.4f;
            w.equipTime = 0.65f;
            w.damage = new ZoneDamage { head = 180, torso = 75, legs = 50 };
            w.falloff = new RangeFalloff { falloffStart = 1000f, falloffEnd = 1000f, minMultiplier = 1f };
            w.maxRange = 250f;
            w.spread = new SpreadSettings { baseSpread = 0.3f, movingSpread = 3f, airborneSpread = 7f, bloomPerShot = 0.6f, maxBloom = 2.4f, crouchMultiplier = 0.7f };
            w.recoil = new RecoilSettings { verticalPerShot = 1.4f, maxVertical = 5f, horizontalAmplitude = 0.35f, resetTime = 0.6f, recoveryRate = 12f };
            w.adsTime = 0.25f;
            w.adsSpreadMultiplier = 0.3f;
            w.adsMoveSpeedMultiplier = 0.7f;
            w.adsFovMultiplier = 0.85f;
            w.viewmodelColor = new Color(0.2f, 0.24f, 0.2f);
            w.viewmodelSize = new Vector3(0.055f, 0.1f, 0.55f);
            w.fireSoundPitch = 0.85f;
            w.fireSoundVolume = 1.1f;
            EditorUtility.SetDirty(w);
        }

        static void ConfigureSniper(WeaponDefinition w)
        {
            w.weaponId = "sniper_rifle";
            w.displayName = "Sniper Rifle";
            w.slot = WeaponSlot.Primary;
            w.fireMode = FireMode.SemiAutomatic;
            w.fireRate = 0.75f;
            w.magazineSize = 5;
            w.reserveAmmo = 15;
            w.reloadTime = 3.2f;
            w.equipTime = 0.8f;
            w.damage = new ZoneDamage { head = 300, torso = 150, legs = 90 };
            w.falloff = new RangeFalloff { falloffStart = 1000f, falloffEnd = 1000f, minMultiplier = 1f };
            w.maxRange = 400f;
            // Inaccurate from the hip, precise only when scoped and still.
            w.spread = new SpreadSettings { baseSpread = 6f, movingSpread = 20f, airborneSpread = 30f, bloomPerShot = 0f, maxBloom = 0f, crouchMultiplier = 0.8f };
            w.recoil = new RecoilSettings { verticalPerShot = 4f, maxVertical = 6f, horizontalAmplitude = 0.3f, resetTime = 1.2f, recoveryRate = 8f };
            w.adsTime = 0.35f;
            w.adsSpreadMultiplier = 0.015f;
            w.adsMoveSpeedMultiplier = 0.5f;
            w.adsFovMultiplier = 0.9f;
            w.viewmodelColor = new Color(0.14f, 0.16f, 0.14f);
            w.viewmodelSize = new Vector3(0.06f, 0.1f, 0.65f);
            w.fireSoundPitch = 0.55f;
            w.fireSoundVolume = 1.4f;
            EditorUtility.SetDirty(w);
        }

        static void ConfigureLaser(AttachmentDefinition a)
        {
            a.attachmentId = "laser";
            a.displayName = "Laser Sight";
            a.slot = AttachmentSlot.Underbarrel;
            a.hipSpreadMultiplier = 0.7f;
            a.visibleLaser = true;
            a.laserColor = new Color(1f, 0.12f, 0.08f);
            a.viewmodelOffset = new Vector3(0.035f, -0.03f, 0.12f);
            a.viewmodelSize = new Vector3(0.025f, 0.025f, 0.08f);
            a.viewmodelColor = new Color(0.12f, 0.12f, 0.12f);
            EditorUtility.SetDirty(a);
        }

        static void ConfigureOptic(AttachmentDefinition a, string id, string name, ReticleStyle style, float fov, float adsTime, float adsSpread,
            bool magnified, Color reticle, Vector3 size)
        {
            a.attachmentId = id;
            a.displayName = name;
            a.slot = AttachmentSlot.Optic;
            a.reticle = style;
            a.aimFovMultiplier = fov;
            a.adsTimeMultiplier = adsTime;
            a.adsSpreadMultiplier = adsSpread;
            a.magnified = magnified;
            a.reticleColor = reticle;
            a.viewmodelOffset = new Vector3(0f, 0.075f, 0.02f);
            a.viewmodelSize = size;
            a.viewmodelColor = new Color(0.08f, 0.08f, 0.09f);
            EditorUtility.SetDirty(a);
        }

        static void ConfigureSmoke(SmokeGrenadeDefinition s)
        {
            s.abilityId = "smoke_grenade";
            s.displayName = "Smoke Grenade";
            s.description = "Throw a grenade that bursts into a vision-blocking cloud.";
            s.cooldown = 1f;
            s.maxCharges = 2;
            EditorUtility.SetDirty(s);
        }

        static void ConfigureWayblocker(WayblockerDefinition w)
        {
            w.abilityId = "wayblocker";
            w.displayName = "Wayblocker";
            w.description = "Deploy a destructible wall that blocks movement and bullets.";
            w.cooldown = 1f;
            w.maxCharges = 1;
            EditorUtility.SetDirty(w);
        }

        static void ConfigureHeal(HealAbilityDefinition h)
        {
            h.abilityId = "field_heal";
            h.displayName = "Field Heal";
            h.description = "Instantly restore 50 health.";
            h.cooldown = 30f;
            h.maxCharges = 0;
            h.healAmount = 50;
            EditorUtility.SetDirty(h);
        }

        static void ConfigureStriker(AgentDefinition a)
        {
            a.agentId = "watchdog";
            a.codename = "WatchDog";
            a.callsign = "Striker";
            a.realName = "Thomas Schmidt";
            a.origin = "Germany";
            a.biography = "A former Bundeswehr soldier and tactical specialist who led a special operations unit " +
                          "before a classified operation resulted in its dissolution.";
            a.unlockedByDefault = true;
            a.health = new Health.HealthProfile();
            a.movement = new Movement.MovementSettings();
            a.accentColor = new Color(1f, 0.69f, 0.25f);
            EditorUtility.SetDirty(a);
        }

        // ───────────── Prefabs ─────────────

        static GameObject CreatePlayerPrefab()
        {
            var go = new GameObject("Player") { layer = GameLayers.Player };
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.minMoveDistance = 0f;
            cc.skinWidth = 0.03f;
            go.AddComponent<NetworkObject>();
            go.AddComponent<PlayerAvatar>(); // Pulls in PlayerSimulation, PlayerHealth, AbilityController.
            go.AddComponent<PlayerPresenter>();
            return Save(go, Prefabs + "/Players/Player.prefab");
        }

        static GameObject CreateWayblockerPrefab()
        {
            var go = new GameObject("Wayblocker") { layer = GameLayers.Deployable };
            go.AddComponent<BoxCollider>().size = new Vector3(3.2f, 2.1f, 0.35f);
            go.AddComponent<NetworkObject>();
            go.AddComponent<Wayblocker>();
            var hb = go.AddComponent<HitboxCollider>();
            hb.zone = HitZone.Torso;
            return Save(go, Prefabs + "/Abilities/Wayblocker.prefab");
        }

        static GameObject CreateSimplePrefab<T>(string path, int layer) where T : Component
        {
            var go = new GameObject(Path.GetFileNameWithoutExtension(path)) { layer = layer };
            go.AddComponent<NetworkObject>();
            go.AddComponent<T>();
            return Save(go, path);
        }

        static GameObject Save(GameObject go, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            AssignGlobalObjectIdHash(prefab);
            return prefab;
        }

        /// <summary>
        /// Netcode computes a prefab's GlobalObjectIdHash in NetworkObject.OnValidate, which does not run for a prefab
        /// asset created from script in the same session. Invoke it explicitly so the hash is serialized.
        /// </summary>
        static void AssignGlobalObjectIdHash(GameObject prefab)
        {
            var networkObject = prefab.GetComponent<NetworkObject>();
            if (networkObject == null) return;
            typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(networkObject, null);
            EditorUtility.SetDirty(networkObject);
            AssetDatabase.SaveAssetIfDirty(prefab);
            uint hash = networkObject.PrefabIdHash;
            if (hash == 0) throw new System.InvalidOperationException($"Netcode did not assign a GlobalObjectIdHash to {prefab.name}");
        }

        static void CreateNetworkPrefabsList(GameContent content)
        {
            NetworkPrefabsList list = LoadOrCreate<NetworkPrefabsList>(NetworkPrefabsPath, out _);
            foreach (GameObject prefab in content.NetworkPrefabs)
            {
                if (prefab != null && !list.Contains(prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });
            }
            EditorUtility.SetDirty(list);
        }

        // ───────────── UI ─────────────

        static void CreatePanelSettings()
        {
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            PanelSettings panel = LoadOrCreate<PanelSettings>(PanelSettingsPath, out _);
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 1f;
            EditorUtility.SetDirty(panel);
        }

        // ───────────── Materials ─────────────

        static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit");

        public static Material CreateLit(string name, Color color, float smoothness = 0.25f)
        {
            Material m = LoadOrCreateMaterial(name, LitShader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material CreateEmissive(string name, Color color)
        {
            Material m = LoadOrCreateMaterial(name, LitShader);
            m.SetColor("_BaseColor", color);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * 1.5f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material CreateTransparent(string name, Color color)
        {
            Material m = LoadOrCreateMaterial(name, LitShader);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_ReceiveShadows", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material CreateUnlit(string name, Color color)
        {
            Material m = LoadOrCreateMaterial(name, Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LoadOrCreateMaterial(string name, Shader shader)
        {
            string path = $"{Materials}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            return m;
        }

        // ───────────── Helpers ─────────────

        public static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
