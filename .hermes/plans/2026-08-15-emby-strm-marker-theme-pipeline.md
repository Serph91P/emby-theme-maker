# Emby STRM marker and theme pipeline implementation plan

Date: 2026-08-15
Design: `docs/superpowers/specs/2026-08-15-emby-strm-marker-theme-pipeline-design.md`
Fork issue: https://github.com/Serph91P/emby-theme-maker/issues/3
Upstream issue: https://github.com/Oratorian/emby-theme-maker/issues/1
Issue author: Serph91P

## Phase 1: Baseline and tests

1. Inspect `ThemeEngine`, `FfmpegRunner`, options, scheduled tasks and existing tests.
2. Record the clean base commit, build command and test command.
3. Add resolver unit tests that initially fail for valid and invalid STRM wrappers.
4. Add an integration seam proving Preview and marker tasks never call the resolver.
5. Run the focused tests and capture the expected red state.

## Phase 2: Safe STRM resolver

1. Add a small resolver with a bounded reader and URI allowlist.
2. Keep direct local media behavior unchanged.
3. Call the resolver only inside Generate after marker and output eligibility checks.
4. Pass the resolved value through `ProcessStartInfo.ArgumentList` or the target framework's equivalent safe argument handling, never through a shell.
5. Redact target-bearing FFmpeg errors before logging.
6. Add cancellation and temporary-file cleanup tests.
7. Run focused tests to green.

## Phase 3: Bounded generation settings

1. Set provider-safe default parallelism to one.
2. Add or validate a maximum newly generated themes per run.
3. Ensure the limit counts successful new sidecars, not skipped existing files.
4. Keep Preview unlimited or give it an independent metadata-only limit.
5. Expose clear settings text that Generate opens media while marker tasks do not.
6. Add tests for limits, overwrite protection and deterministic selection.

## Phase 4: Documentation and compatibility

1. Document the preferred marker priority.
2. Document the official TheIntroDB plugin as the central online marker source.
3. Leave Theme Maker's online marker tasks manual and without default triggers.
4. Document EmbyCredits as local-media-only in this deployment architecture.
5. Add migration notes for version 1.1.0 users.
6. Confirm `netstandard2.0` and Emby Server Core compatibility.

## Phase 5: Repository verification and PRs

1. Run restore, release build and the complete test suite.
2. Run `git diff --check`.
3. Scan changed files for U+2013 and U+2014.
4. Run dependency vulnerability checks.
5. Run a secret scan over the exact branch range.
6. Request independent code and security review.
7. Fix all accepted high and medium findings.
8. Commit only relevant files and push the feature branch.
9. Open a PR in `Serph91P/emby-theme-maker` linked to issue 3.
10. Open an upstream PR against `Oratorian/emby-theme-maker` linked to upstream issue 1.
11. Verify the live PR head and checks before merge.

## Phase 6: Official TheIntroDB plugin integration

1. Inspect the current official release and source at `TheIntroDB/emby-plugin`.
2. Verify signatures or hashes and build compatibility with Emby 4.9.5.0.
3. Audit settings, marker merge semantics, repair behavior and schedules.
4. Back up Emby plugins, plugin configuration and chapter state.
5. Install the plugin using the supported Emby plugin path.
6. Restart Emby once and verify version, loading, API visibility and logs.
7. Configure only metadata marker retrieval for intro and credits.
8. Run a read-only or single-series pilot if supported.
9. Apply to one pilot series and verify chapter changes through Emby API and a database snapshot.
10. Confirm no STRM access or media URL appears in logs.

## Phase 7: EmbyCredits isolation

1. Inspect current EmbyCredits rules and selected libraries.
2. Prove which paths contain local media and which contain STRM wrappers.
3. Configure EmbyCredits to process local media only.
4. Preserve existing `CreditsStart` markers.
5. Run a one-series local-media pilot.
6. Verify no STRM path was queued, probed or opened.

## Phase 8: Theme Maker deployment and Cyberpunk acceptance

1. Build and hash the reviewed Theme Maker artifact.
2. Back up the installed DLL and configuration.
3. Deploy through the existing Emby ownership and mount path.
4. Restart Emby and verify plugin version, tasks, configuration and logs.
5. Limit Generate to `Cyberpunk: Edgerunners`, one job and one output.
6. Run Preview and confirm no media access.
7. Run Generate and monitor the selected wrapper and FFmpeg process without logging its target.
8. Validate `theme.mp3` with FFprobe, size, duration, placement and Emby refresh.
9. Test the real Emby user path.
10. Restore normal production scope.

## Phase 9: Contributor upload pilot

1. Inspect `themesongs.dll`, its live configuration and task behavior.
2. Verify `JoinContributorsNetwork` and upload-history semantics.
3. Snapshot `Themeitems`, `UploadedThemeitems`, sidecar state and logs.
4. Trigger the smallest supported contributor processing path for the Cyberpunk sidecar.
5. Verify explicit upload success and upload-history change without exposing endpoints or credentials.
6. If upload evidence is absent, stop and document the exact blocker instead of assuming success.

## Phase 10: Night schedule and broad rollout

1. Inventory existing Emby task triggers and measured durations.
2. Choose non-overlapping Europe/Berlin times in the required dependency order.
3. Keep TheIntroDB marker tasks metadata-only.
4. Keep EmbyCredits local-only.
5. Set Theme Maker `Jobs=1` and a conservative per-run generation cap.
6. Estimate candidate count, data volume and total runtime before the first broad run.
7. Request explicit approval for the estimated multi-hour rollout.
8. Enable bounded nightly runs.
9. After each run verify new sidecars, errors, provider concurrency, Emby health and upload evidence.
10. Stop automatically on provider throttling, repeated FFmpeg failures, invalid sidecars or database health regressions.

## Final acceptance

1. Re-read both GitHub issues and this plan line by line.
2. Verify every acceptance criterion with fresh live evidence.
3. Verify reviewed commits are contained in the intended branches.
4. Comment on and close implemented issues according to repository lifecycle.
5. Report changed configuration, installed versions, schedules, produced themes, uploaded themes, failures and any explicitly untested layer.
