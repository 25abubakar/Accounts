# Accounts SQL layout

| Folder | What belongs here | Git |
|--------|-------------------|-----|
| `StoredProcedures/` | Production `usp_*` / Wave list SPs | Tracked; also ship via EF migrations |
| `Apply/` | One-shot menu/module apply scripts | Tracked |
| `Fixes/` | One-shot data/schema fixes | Tracked |
| `Seeds/` | Optional demo/seed scripts | Tracked |

Scratch dumps (`scratch_sp.sql`, `out.sql`, …) live under repo `_dev/scratch/` and are **gitignored**.
