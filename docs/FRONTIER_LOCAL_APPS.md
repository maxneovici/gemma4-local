# Frontier Local LLM App Playbook

This template is designed for fast local AI experiments that can grow into deployable applications.

## Fast Experiment Loop

1. Add or edit an agent in `LocalAi:Agents`.
2. Add a local tool under `src/Gemma4Local.Api/Tools`.
3. Register the tool in `Program.cs`.
4. Test through Swagger at `/swagger`.
5. Persist findings to memory with `/memory/upsert` or the `memory_write` tool.
6. Promote stable experiments into typed services and endpoints.

## Agent Patterns

- Use one `coordinator` for routing and synthesis.
- Use specialist agents for bounded tasks: `researcher`, `coder`, `critic`, `planner`, `tester`.
- Keep `AllowedTools` narrow per agent.
- Keep `AllowedAgents` explicit to avoid uncontrolled delegation loops.
- Add recursion/depth limits before enabling automatic multi-step delegation in production.

## Tool Patterns

- Tools are local capabilities, not magic model features.
- Prefer strongly typed service code behind each tool.
- Keep tool outputs compact because they are fed back into the model context.
- Treat tool output as untrusted input.
- Use native OpenAI/Ollama tool calling later if model-specific behavior is stable enough for your use case.

## Memory Patterns

- Short-term memory can stay in process.
- Long-term memory should use Qdrant.
- Use dedicated embedding models, not chat models, for vector generation.
- Store source, timestamp, agent, and conversation metadata with each memory record.
- Consider separate collections per agent plus a shared `global` collection.

## Local Model Strategy

- Use `gemma4:e2b` for quick loops and cheap agent routing.
- Use `gemma4:e4b` for better small-model reasoning.
- Use `gemma4:26b` for workstation MoE experiments.
- Use `gemma4:31b` for the strongest local dense model when latency and RAM are acceptable.
- Use `nomic-embed-text` for embeddings.

## Build Agents That Build

This template includes a disabled-by-default `safe_shell_command` tool. Enable it only when you want local builder/tester agents to run exact allowlisted commands.

Default allowlisted commands:

- `dotnet build`
- `dotnet test`
- `git status --short`
- `git diff --stat`

Additional local tools to add incrementally:

- `workspace_read` for reading approved files.
- `workspace_search` for searching approved paths.
- `git_diff` for reviewing local changes.
- `test_runner` for structured verification.

Do not expose arbitrary shell execution without an allowlist and human approval. The goal is local autonomy, not unsafe automation.

## Suggested Next Agent Roles

- `planner`: decomposes a feature into safe local steps.
- `builder`: proposes implementation details and invokes build checks.
- `tester`: runs allowlisted tests and summarizes failures.
- `critic`: reviews output for risks and missing verification.
- `memory-curator`: decides what should be persisted into vector memory.

## Production Hardening

- Put auth in front of the API before exposing it beyond localhost.
- Keep `RequireLoopback=true` for host-local inference.
- If using Docker service names, intentionally set `RequireLoopback=false` only inside a trusted Docker network.
- Add rate limiting for expensive model calls.
- Add structured logs for agent/tool calls.
- Persist agent traces for reproducibility.
