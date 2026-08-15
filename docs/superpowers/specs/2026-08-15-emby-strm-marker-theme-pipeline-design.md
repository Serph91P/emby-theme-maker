# Emby STRM marker and theme pipeline design

Date: 2026-08-15
Status: Approved for implementation
Canonical fork issue: https://github.com/Serph91P/emby-theme-maker/issues/3
Canonical upstream issue: https://github.com/Oratorian/emby-theme-maker/issues/1
Issue author: Serph91P

## Goal

Provide safe intro and credits markers for local and STRM episodes, then generate missing `theme.mp3` sidecars from STRM sources during a bounded night window without exceeding the provider limit of three concurrent streams.

## Ownership boundaries

### Existing Emby markers

Existing complete intro and credits markers always win. No component overwrites a complete or partial marker set without an explicit repair mode.

### Theme Maker

Theme Maker owns:

- safe local reference inheritance for missing intro markers
- read-only marker preview
- theme readiness preview
- actual `theme.mp3` generation from stored intro markers
- safe STRM wrapper resolution only during Generate
- bounded nightly generation with one concurrent media stream

Theme Maker does not become the central online marker service. Its existing online fallback remains manual and unscheduled for compatibility, while the official TheIntroDB plugin is the preferred online source.

### Official TheIntroDB Emby plugin

The official TheIntroDB plugin owns online metadata marker retrieval for:

- intro
- credits
- recap
- preview

Its marker scan must not open STRM wrappers or referenced media targets. It writes validated markers through supported Emby APIs and runs before stream-opening tasks.

### EmbyCredits

EmbyCredits remains responsible for analysis of real local media files. STRM episodes are excluded from its FFmpeg, OCR, chromaprint and black-frame detection paths. The official TheIntroDB plugin supplies online credits markers for STRM episodes.

### Theme Songs contributor flow

Theme Songs owns contributor upload behavior. The production flow verifies its exact trigger and upload history before relying on it. Theme Maker writes exact non-empty `theme.mp3` files and requests an Emby refresh. Upload execution runs only after generated sidecars have been validated.

## Data flow

1. Existing Emby chapter markers are read.
2. Theme Maker may inherit a missing intro marker pair from an unambiguous local duplicate matched by provider identity, season and episode.
3. The official TheIntroDB plugin supplies remaining online intro and credits markers without media access.
4. Emby persists markers in its chapter repository.
5. Theme Maker readiness preview selects series that have a valid intro pair and no existing `theme.mp3`, without reading STRM content.
6. Theme Maker Generate resolves one selected STRM wrapper and opens its validated target through FFmpeg.
7. FFmpeg extracts only the stored intro interval and atomically creates `theme.mp3` in the series directory.
8. Emby refreshes the affected series.
9. Theme Songs contributor processing uploads only validated generated sidecars and records upload evidence.

## STRM resolver security contract

The resolver is called only by Generate after all metadata-only eligibility checks pass.

It must:

- accept normal local media paths unchanged
- detect `.strm` case-insensitively
- reject wrappers larger than a small fixed byte limit
- decode UTF-8 with optional BOM
- ignore blank lines and documented comment lines
- require exactly one usable target
- require an absolute URI
- allow `http` and `https` by default
- reject file, shell, pipe and other schemes
- never execute through a shell
- never log the target, authority, path, query, fragment or embedded credentials
- return only a safe error category and item identity
- honor task cancellation

Credentials present in a target may be passed directly to FFmpeg as opaque data but must never be emitted in logs or summaries.

## Generation safety

- Default `Jobs` is 1.
- The production schedule uses one FFmpeg process at a time.
- A configurable maximum number of newly generated themes per run bounds runtime and provider use.
- Existing `theme.mp3` files are skipped unless overwrite is explicitly enabled.
- Output is written to a temporary sibling file and renamed only after FFprobe validates it.
- Cancellation or failure removes only the task-owned temporary file.
- No cleanup removes pre-existing or externally managed sidecars.
- The first live acceptance remains limited to `Cyberpunk: Edgerunners`.

## Scheduling

Target order in Europe/Berlin time:

1. TheIntroDB metadata marker scan
2. EmbyCredits for local media only
3. Theme Maker readiness preview
4. Theme Maker bounded Generate with `Jobs=1`
5. Targeted Emby refresh
6. Theme Songs contributor processing

Exact clock times are chosen after measuring the live durations and checking existing Emby schedules. Tasks must not overlap with the provider-facing Generate run.

## Failure handling

- Marker lookup failure leaves existing chapters unchanged.
- Ambiguous local reference markers are rejected.
- Invalid STRM wrappers are skipped with a safe reason.
- FFmpeg failure leaves no final theme and no temporary file.
- HTTP 429 or provider instability stops further provider-facing generation in that run.
- Three-stream capacity is treated as an upper bound, not a concurrency target.
- Database health, plugin health and Emby availability are checked after deployment and every pilot.

## Rollout

1. Implement and test the resolver and bounded generation in the fork.
2. Open a linked upstream PR.
3. Inspect and package the official TheIntroDB plugin release.
4. Back up Emby plugin and configuration state.
5. Install TheIntroDB and verify marker-only behavior on one series.
6. Configure EmbyCredits to local media only.
7. Deploy the Theme Maker build and run the Cyberpunk pilot.
8. Verify output format, placement, logs, refresh and real playback path.
9. Verify contributor upload behavior with one generated sidecar.
10. Estimate total missing-theme count and runtime.
11. Obtain explicit approval for the broad multi-hour generation window.
12. Enable bounded nightly batches and monitor completion.

## Acceptance criteria

- Marker-only tasks do not read STRM wrappers or open media targets.
- Existing markers and non-marker chapters remain intact.
- STRM credits are supplied through metadata, not EmbyCredits media analysis.
- The Cyberpunk pilot creates a valid `theme.mp3` from stored markers.
- Provider concurrency never exceeds one during Theme Maker generation.
- Logs contain no media URLs or credentials.
- The sidecar is directly in the series folder and Emby can read it.
- Contributor upload has explicit live evidence before broad rollout.
- Normal production configuration is restored after every pilot.
