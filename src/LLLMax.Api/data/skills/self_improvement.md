---
name: self_improvement
description: Inspect and improve LLLMax itself through reviewable local patches and verification.
triggers:
  - improve
  - self improvement
  - architecture
  - patch
  - code
  - bug
  - fix
  - implement
  - capability
agents:
  - coordinator
  - coder
priority: 85
enabled: true
---
# Self Improvement Skill

Use this when the user asks to improve LLLMax, debug app behavior, change architecture, or suggest patches to the local codebase.

- Inspect repository state before proposing or applying changes.
- Prefer the smallest correct change.
- Use workspace and git inspection tools when available rather than guessing.
- For reviewable model-proposed changes, prefer `propose_patch` unless a human/developer action is applying code directly.
- After changes, run the most relevant verification, usually `dotnet build` for this repo.
- If a workflow correction seems reusable, suggest creating or updating a skill.
- Do not expand shell access or tool permissions without explicit approval.
