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

- If the target is personal or implicit, such as "my favorite sites" or "news I care about", use `memory_search` first to recover relevant sites, sources, topics, or user interests.
- Use `web_browse` for each concrete URL or source needed to answer the request.
- For news requests, summarize concrete article/headline candidates rather than broad site themes. Group by source when several sites are browsed, include 2-4 specific items per source when available, and explain each item in one short sentence.
- If the user asks for categories, keep each source separate first, then put category bullets under that source. Example: `DN.se` -> `Politics`, `Crime`, `Culture`; `Aftonbladet.se` -> `Politics`, `Crime`, `Entertainment`; `SVT.se` -> `Politics`, `Health`, `International`.
- Keep category summaries compact: at most 3 category bullets per source, one distinct story per bullet, no repeated filler. If a source has fewer reliable items, list fewer.
- Use `web_browse` directly for ordinary news headlines, Reddit pages, docs, and simple web summaries. Do not use deep research for these.
- Use `deep_research_web` only when the user explicitly wants long-running background research across multiple pages, a durable report, or work that should continue after the chat turn.
- Never call `web_research`; it is a background job kind, not a foreground tool.
- Do not stop after finding remembered targets; continue browsing when the user asked to check or summarize current information.
- Prefer a small number of high-signal sources over broad unfocused browsing.
- Summarize only after tool results are available.
- Cite web URLs in the final answer, preferably beside each source group or item.
- If a page cannot be fetched or is too sparse, say so and continue with the sources that worked.
