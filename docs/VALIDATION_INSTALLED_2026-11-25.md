# Installed observer trace (Nov 25, 2026)

This report summarizes the latest test run in the uploaded appended JSONL file. The raw file is **not committed**, to avoid exposing process and device identifiers.

The installed observer started at 15:01:35 UTC in read-only mode. Steam host log was attached.

| UTC | Event |
| --- | --- |
| 15:03:30 | Candidate SudoVDA activation before Steam confirms |
| 15:03:32 | Steam Start/GameSelected, then stable 1280x800 virtual display; Streaming |
| 15:04:04-08 | Steam Completed, Disconnecting, then Desktop after the physical display stabilized |
| 15:04:12 | Unexpected Desktop to Disconnecting during reconnect due to stale completed marker |
| 15:04:14-17 | Fresh Steam start and confirmed Streaming on reconnected SudoVDA |
| 15:06:06-12 | Steam Completed; Disconnecting; then Desktop on restored physical monitor |

There are no observer error or steam_log_gap events in the latest run. The transient Desktop-to-Disconnecting transition was a state-machine bug, not a streaming failure. The reducer now prevents a stale end marker from taking an already-Desktop machine back into Disconnecting, and treats an unconfirmed new virtual monitor as Candidate. Two regression checks cover these cases.

The log supports correct installed-app detection, but does not prove Windows-login auto-start or running-app MSI upgrade.
