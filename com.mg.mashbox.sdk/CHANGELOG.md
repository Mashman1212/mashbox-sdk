# MashBox SDK Patch Notes

## [Unreleased]

### Fixed

- Ported the Project X U6 HDRP color-grading LUT refresh workaround to the shared SDK. Unity 6 editor and player cameras invalidate the cached LUT before rendering so tonemapping and volume color adjustments refresh. This rebuilds the LUT per camera render; Unity 2022 projects are unaffected.

## [0.14.13] - 2026-08-18

### Added

- Added a MashBox developer-only Leaderboard Generator for producing Daily, Weekly, Monthly, and All-Time UGS `.lb` deployment configurations.
- Added companion MashBox leaderboard manifests describing score direction, update strategy, score units, activity type, and map/level/PvP availability metadata.

### Changed

- Bumped MashBox SDK package version from 0.14.12 to 0.14.13.

## [0.5.60] - 2026-05-22

### Added

- Added Human Full Skin content validation rules with 2K texture slots and a 10 MB item texture budget.
- Added visible item texture budget labels to the Content Builder validation rules display.

### Changed

- Full Skin content packs must contain only Human Full Skin items.
- Full Skin-only packs now validate against a 10 MB pack size and texture budget for mod.io publishing instead of the default 3 MB pack size target.
- Bumped MashBox SDK package version from 0.5.59 to 0.5.60.

## [0.5.56] - 2026-05-19

### Changed

- Updated Simple FMOD Bank Loader to load on start and unload on disable by default.
- Bumped MashBox SDK package version from 0.5.55 to 0.5.56.

## [0.5.55] - 2026-05-19

### Added

- Added Simple FMOD Bank Loader so map makers can load and unload FMOD banks by string name without requiring an FMOD compile-time dependency.

### Changed

- Bumped MashBox SDK package version from 0.5.54 to 0.5.55.

## [0.5.54] - 2026-05-19

### Added

- Added Simple FMOD Event Audio for map makers to start, stop, and restart an FMOD event by string path from UnityEvents.

### Changed

- Bumped MashBox SDK package version from 0.5.53 to 0.5.54.

## [0.5.53] - 2026-05-14

### Changed

- Bumped MashBox SDK package version from 0.5.52 to 0.5.53.

## [0.5.52] - 2026-05-13

### Added

- Added a Patch Notes tab to MashBox SDK Setup so users can read package release notes inside Unity.
- Added this package changelog as the source of truth for version descriptions shown in the SDK tools.

### Changed

- Bumped MashBox SDK package version from 0.5.51 to 0.5.52.
