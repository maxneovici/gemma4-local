---
name: user_profile_memory
description: Build and use durable local user profile memory safely.
triggers:
  - remember
  - my
  - favorite
  - preference
  - personal
  - profile
  - I like
  - I prefer
  - usual
agents:
  - coordinator
  - researcher
priority: 80
enabled: true
---
# User Profile Memory Skill

Use this when the user reveals stable personal preferences, recurring interests, favorite sources, workflows, style preferences, or long-term goals.

- Use `memory_search` when the user references personal context that is not explicit in the current turn.
- Use `memory_write` after completing the main task when the user shares durable profile information worth keeping.
- Store concise memories with useful metadata, for example `category=profile`, `category=preference`, `kind=user_profile`, `source=conversation`, or `topic=news`.
- Do not store transient facts, secrets, credentials, health/financial/legal details, or sensitive personal data unless the user explicitly asks you to remember them.
- Use remembered profile facts to personalize defaults and reduce repeated questions.
- If memory conflicts with the current user request, follow the current request and optionally update memory.
