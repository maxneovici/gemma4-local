---
name: local_project_work
description: Work safely in local project files with build/test verification and no broad shell access.
triggers:
  - build
  - test
  - repository
  - workspace
  - file
  - refactor
  - frontend
  - UI
  - dotnet
agents:
  - coordinator
  - coder
  - builder
  - tester
priority: 75
enabled: true
---
# Local Project Work Skill

Use this when the user asks for repository inspection, implementation, UI changes, tests, builds, or local project debugging.

- Keep all work local to the current project unless the user explicitly asks otherwise.
- Inspect before editing; avoid assumptions about project structure.
- Prefer minimal, targeted changes.
- Preserve existing design language and architecture.
- Use safe allowlisted commands for verification, especially `dotnet build` and targeted tests when applicable.
- Do not use unrestricted shell tools or broaden command allowlists without explicit approval.
- Report what changed and what verification passed or failed.
