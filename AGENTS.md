# LLLMax Agents

This repo is a template for local-only LLM experiments. Keep all inference, embeddings, tool execution, and vector memory on loopback or local Docker services unless the user explicitly changes the architecture.

## Local-Only Rules

- Keep `LocalAi:RequireLoopback=true` by default.
- Allowed inference endpoints are `http://127.0.0.1:*` and `http://localhost:*`.
- Do not add OpenAI, Azure OpenAI, Gemini, Anthropic, OpenRouter, or Ollama Cloud keys to this baseline.
- Avoid cloud model tags such as `*:cloud` when the goal is local-only inference.

## Agent Architecture

- Agent definitions live in `LocalAi:Agents` configuration and map to `AgentDefinition`.
- `IAgentRuntime` owns agent execution.
- `IAgentRegistry` owns configured agent lookup.
- `ILocalToolRegistry` owns tool discovery.
- `ILocalTool` implementations must be deterministic local operations unless clearly named otherwise.
- Agent-to-agent communication happens through the `delegate_to_agent` tool.

## Tool Calling

- Native Ollama/OpenAI tool calling can be added later, but the baseline uses a JSON tool protocol to keep provider behavior visible and debuggable.
- Tool output should be treated as untrusted input when fed back to a model.
- Each tool must declare a concise JSON schema in `ArgumentsJsonSchema`.
- Agents must only call tools listed in their `AllowedTools`.

## Memory

- `ILocalMemoryStore` is the storage abstraction.
- `IEmbeddingGenerator` is the embedding abstraction.
- The default `InMemoryVectorStore` is for experiments and tests only.
- Use Qdrant via `docker-compose.yml` when persistence or larger memory is needed.
- Prefer dedicated embedding models such as `nomic-embed-text`; do not use chat models for embeddings unless there is a concrete reason.

## Extension Points

- Add new tools under `src/LLLMax.Api/Tools` and register them in `Program.cs`.
- Add persistent vector stores under `src/LLLMax.Api/Memory` and bind them behind `ILocalMemoryStore`.
- Keep endpoint handlers thin; business behavior belongs in services.

## High-Leverage Next Tools

- `workspace_search`: search approved local folders.
- `workspace_read`: read approved local files.
- `test_runner`: run allowlisted verification commands.
- `qdrant_memory`: persistent vector memory implementation.
- `agent_trace`: persist agent prompts, tool calls, and responses.

## Safety For Builder Agents

- Do not add unrestricted shell tools by default.
- If shell tools are introduced, require an allowlist and clear logging.
- `safe_shell_command` is present but disabled by default; enable it only with `LocalAi:Tools:EnableSafeShell=true`.
- Never expand `AllowedShellCommands` to broad shells like `bash`, `zsh`, `sh`, `pwsh`, or `python` without human approval and additional sandboxing.
- Keep generated code inside the current project unless the user explicitly asks for cross-repo edits.
- Prefer `dotnet build` and tests as verification gates before reporting success.
