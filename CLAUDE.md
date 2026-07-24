# SimpleERP v3

## Knowledge base

This project's persistent knowledge, learnings, and project notes live in
the Obsidian vault at `C:\Obsidian`, not in this repo. Always read and write
there:

- Project notes: `C:\Obsidian\projects\simpleerp\overview.md` (and
  `decisions.md`, `questions.md`, `phase-plan.md` as they're created)
- Reusable/general concepts: `C:\Obsidian\knowledge\`
- Vault conventions: `C:\Obsidian\README.md` — read this first if you're
  about to create or edit a note (atomic notes in `/knowledge`, project logs
  in `/projects`, link both ways per the extraction rule)

Before starting substantial work, check
`C:\Obsidian\projects\simpleerp\overview.md` for current status. After
learning something reusable or making a non-obvious decision, write it to
the vault (extract general concepts to `/knowledge`, log project-specific
decisions to `/projects/simpleerp/decisions.md`) rather than leaving it only
in this session.

## Project snapshot

ASP.NET Core (net8.0) ERP, Clean Architecture (`Domain` / `Application` /
`Infrastructure` / `Web`), PostgreSQL (EF Core migrations; connection string
in user-secrets), Razor Pages. Full
business spec: `SimpleERP_ProjectMasterDocument.docx` in this repo root.
Architecture/tech details: `C:\Obsidian\projects\simpleerp\overview.md`.
