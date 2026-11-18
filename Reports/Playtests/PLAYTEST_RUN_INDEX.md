# 60-Day Playtest Run Index

The Unity Editor runner records a durable run directory per attempt. See `PLAYTEST_60_DAY_PROTOCOL.md` for execution and interpretation.

| Run ID | Result | Days | Source | Artifact location |
|---|---|---:|---|---|
| `20261117T214151Z-ac7b3530` | PASS, pre-sawmill workstation fix | 60/60 | Muse tip `8cb914eaa7facbf882b40a27b4aae3cdd007198e` | `Runs/20261117T214151Z-ac7b3530/` |
| `20261117T220225Z-e0f61aea` | PASS, after Codex workstation-owner lookup fix | 60/60 | Muse tip plus uncommitted Codex source fix | `Runs/20261117T220225Z-e0f61aea/` |

Two earlier runner attempts are retained locally but excluded from this index's completed runs: `20261117T212512Z-57cdf3de` (0/60; runner transition-state defect) and `20261117T213318Z-715bf690` (invalid after one observed day; startup timeout remained active). They are not presented as successful simulation evidence.

The second completed run has 3,040 logs, 1,223 warnings, zero errors and zero exceptions. It verifies the sawmill workstation key lookup progresses to the next labor gate, where the assigned task is refused because worker P11 has no scheduled minutes available. It does not verify lumber production.

The owner's supplied short-playtest evidence was summarized from the request; no raw export from that run was available, so it is not represented as a captured artifact.
| 20261118T134545Z-19cb1de9 | INVALID | 0/60 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T134545Z-19cb1de9` |
| 20261118T140203Z-002f010d | PASS | 60/60 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T140203Z-002f010d` |
| 20261118T143139Z-1e177b1b | PASS | 60/60 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T143139Z-1e177b1b` |
| 20261118T150308Z-24fdc55d | PASS | 60/60 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T150308Z-24fdc55d` |
| 20261118T152127Z-a364bc75 | PASS | 60/60 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T152127Z-a364bc75` |
| 20261118T153611Z-9ce9a0ce | PASS | 60/60 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T153611Z-9ce9a0ce` |
| 20261118T160808Z-fc53cfb5 | PASS | 60/60 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T160808Z-fc53cfb5` |
| 20261118T162831Z-e58da6e5 | PASS | 3/3 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T162831Z-e58da6e5` |
| 20261118T164247Z-63bb4526 | INVALID | 0/60 | `codex/player-economic-participation-2026-11-18` | `52584eb559a41bf9ddd20c4bd3249fe9b67c448d` | `F:\Land & Ledgers\Reports/Playtests/Runs\20261118T164247Z-63bb4526` |
