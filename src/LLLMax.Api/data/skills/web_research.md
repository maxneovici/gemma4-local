---
name: web_research
description: Research current web pages, headlines, docs, Reddit threads, and other live sources with citations.
triggers:
  - research
  - latest
  - headlines
  - website
  - web
  - reddit
  - news
  - summarize
  - current
agents:
  - coordinator
  - researcher
priority: 90
enabled: true
---
# Web Research Skill

Use this when the user asks for current external information, headlines, Reddit activity, documentation, or website summaries.

- If the target is personal or implicit, such as "my favorite sites", use `memory_search` first to recover the target URLs or names.
- Use `web_browse` for each concrete URL or source needed to answer the request.
- Use `web_browse` directly for ordinary news headlines, Reddit pages, docs, and simple web summaries. Do not use deep research for these.
- Use `deep_research_web` only when the user explicitly wants long-running background research across multiple pages, a durable report, or work that should continue after the chat turn.
- Never call `web_research`; it is a background job kind, not a foreground tool.
- Do not stop after finding remembered targets; continue browsing when the user asked to check or summarize current information.
- Prefer a small number of high-signal sources over broad unfocused browsing.
- Summarize only after tool results are available.
- Cite web URLs in the final answer.
- If a page cannot be fetched or is too sparse, say so and continue with the sources that worked.
