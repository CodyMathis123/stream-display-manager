# Steam Remote Play host log evidence — November 25, 2026

The user supplied streaming_log.txt and streaming_log.previous.txt to validate session-detection behavior. The full logs are intentionally NOT included in source control because they can expose client IP addresses, controller identifiers, game names, window titles, local paths and device identifiers.

## Verified signatures in current Steam host build

- Session start: \`Streaming started to steamdeck at [client address]:[port], ...\`
- App identity follows start: \`Game: Starfield (1716740)\` (Steam App ID)
- Session completion: \`Encoding complete\`
- Game window announcement: \`Streamed game has created a window\` (informational only)
- \`Maximum capture: 2560x1440 59.00 FPS\` is a **capability limit**, not the actual 1280x800 video size.

The log contains a host-side client address and unrelated captured desktop window titles. Our app must **never copy raw lines into the observer JSONL**. It emits only signature kind, sanitized client class, numeric App ID and observed session ordinal.

## Correlation with display observer (local time)

| Time | Steam log | Display/window behavior |
| --- | --- | --- |
| 09:10:39 | Streaming started to steamdeck; Starfield App ID 1716740 | SudoVDA DISPLAY6 becomes 1280x800 primary |
| 09:11:18 | Encoding complete | Starfield remains running and returns to physical display |
| 09:11:28 | Streaming started to steamdeck; Starfield App ID 1716740 | Same Starfield HWND returns to virtual DISPLAY6 |
| 09:11:40 | Encoding complete | Game exits and desktop returns to physical primary |

The completion event is a useful observed end marker in this build, but the log's text format is not a documented stable API. It must be correlated with stable desktop display topology, and missing/conflicting evidence produces Unknown/Candidate rather than authorization to move a window.

## Avoid false positives

- \`Streaming initialized and listening on port 27031\` is not a confirmed client connection.
- \`Game Recording - game stopped\` is not a confirmed stream end; Steam records local game lifecycles separately.
- \`Lost Video Stream\`, \`>>> Stopped desktop stream\`, and switching between Desktop_MovieStream / GameOverlay_MovieStream may occur while a Remote Play session remains connected.
- Steam.exe being present and SudoVDA being installed are not enough.
- The log can rotate; the reader starts at EOF and invalidates state on gaps, missing files, rotations or unexpectedly large unread intervals.
- If the observer starts after a stream has already begun, it cannot establish an active session from old log history. Reconnect to generate fresh start evidence.

## Additional performance finding

The provided Starfield sessions used \`hardware_enabled=false\` on the host and \`Game D3D12 NV12 + libx264 main (4 threads)\` capture, while the Steam Deck used VAAPI hardware decoding. Hardware video encoding on the RX 9070 XT should be evaluated as a separate streaming performance improvement before 4K60.

## Validation

Parser tests use sanitized synthetic lines shaped like observed host messages; raw uploads remain off-repository. The Phase 1.5 integration is **read-only** and does not move windows or modify displays.
