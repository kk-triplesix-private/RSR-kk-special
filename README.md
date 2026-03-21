# RSR KK's Special

A private fork of [RotationSolverReborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn) by the FFXIV Combat Reborn team.

I absolutely love and appreciate the original project — the Combat Reborn team has done an incredible job building and maintaining RSR. This fork exists solely because I needed a few very specific customizations for my own personal use.

---

## What's Different

### Custom Rotations

13 custom rotation files in `RotationSolver/ExtraRotations/`:

| Role | Rotation | Description |
|------|----------|-------------|
| **Magical** | **SMN_Dynamic** | Flagship rotation (1,500+ lines) — dynamic egi selection, BossMod IPC integration, smart Addle/Aegis timing, M11S/M12S fight-specific logic, full diagnostics & simulation panel |
| **Magical** | ChurinSMN | Churin's custom Summoner rotation |
| **Magical** | BeirutaPCT | Custom Pictomancer rotation |
| **Magical** | BeirutaRDM | Custom Red Mage rotation |
| **Magical** | Rabbs_BLM_All_Levels | Rabbs' Black Mage rotation for all levels |
| **Healer** | BeirutaAST | Custom Astrologian rotation |
| **Healer** | BeirutaSCH | Custom Scholar rotation |
| **Melee** | ChurinMNK | Churin's custom Monk rotation |
| **Melee** | DSRViper | Viper rotation optimized for DSR |
| **Ranged** | ChurinBRD | Churin's custom Bard rotation |
| **Ranged** | ChurinDNC | Churin's custom Dancer rotation |
| **Ranged** | ChurinMCH | Churin's custom Machinist rotation |
| **Tank** | ChurinDRK | Churin's custom Dark Knight rotation |

### Rotation Timeline Planner

A full rotation planning system integrated with BossMod encounter data:

- Action palette with GCD/oGCD actions
- BossMod encounter dropdown with auto-loaded phase timings and mechanics
- Drag & drop actions onto a visual timeline with slot snapping
- Precast timer with configurable prepull zone
- Cast bars showing skill activation timing
- Mechanic connection lines linking oGCDs to upcoming raidwides/tankbusters

### BossMod IPC Integration

Deep integration with [BossModReborn](https://github.com/FFXIV-CombatReborn/BossmodReborn) for intelligent rotation decisions:

- Raidwide/tankbuster/stack damage prediction for proactive mitigation
- SpecialMode detection (Pyretic, NoMovement, Freezing) for movement-aware ability selection
- ForbiddenDirections for mechanic-safe rotation pausing (e.g. M12S Directed Grotesquerie)

### UI Modernization

- Dark elegant theme with teal accents (RSRStyle.cs)
- Glassmorphism styling with config toggle
- Consistent dark theme across all windows

### Automated Upstream Sync

- Syncs with the original repo every 2 hours automatically
- Preserves all local changes on conflict (merge strategy: ours)
- Auto-increments version tags

---

## Credits & Thanks

Huge thanks to the **FFXIV Combat Reborn team** for creating and maintaining the original [RotationSolverReborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn). Their work is the foundation this fork is built on.

Original repository: https://github.com/FFXIV-CombatReborn/RotationSolverReborn
