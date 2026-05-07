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
- If a workflow correction seems reusable and no matching skill exists, use `create_skill` to create an approval-gated local markdown skill.
- If a matching skill exists but needs a change, use `update_skill` rather than generic workspace writes.
- If skill creation or update requires approval, ask the user to approve it and retry with the returned `approvalId`.
- Use `schedule_background_job` for long-running local verification, large research, document vectorization, or memory reports that should continue after the chat turn returns.
- Current background job kinds include `document_vectorize_folder`, `memory_report`, and `web_research`.
- Do not expand shell access or tool permissions without explicit approval.
