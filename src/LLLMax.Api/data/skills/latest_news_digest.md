---
name: latest_news_digest
description: Build a polished latest-news digest from the user's remembered or explicit news sources.
triggers:
  - latest news
  - get the news
  - favorite news
  - news sources
  - summarize by category
  - headlines
agents:
  - coordinator
priority: 100
enabled: true
---
# Latest News Digest

Use this when the user asks for latest/current news, especially from remembered or favorite sources.

- The fast coordinator must do routing, memory lookup, and browsing.
- This skill is a procedure, not a callable tool. Never emit a tool call named `latest_news_digest`.
- If sources are implicit, use `memory_search` to recover relevant news sites, sources, topics, or user interests.
- Use `web_browse` for each concrete source before synthesis.
- After browsing, delegate final synthesis to `deep_researcher`; do not ask the coordinator model to produce the final digest itself.
- Pass only the browsed source bundle and the user's original request to `deep_researcher`.
- Ask for source-first formatting, then category bullets under each source.
- Require at most 3 category bullets per source, one distinct story per bullet, no repeated filler.
- Require source URLs beside each source group or item.
- Keep the final digest concise and user-facing.
