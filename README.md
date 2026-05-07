# LLLMax

Local-first .NET 9 assistant scaffold for Ollama on Apple Silicon, built around scoped tools, persistent local memory, dynamic subagents, document understanding, and a hosted PWA chat interface.

The app is intentionally configured to use loopback-only Ollama endpoints by default. It does not call OpenAI, Azure OpenAI, Ollama Cloud, or any other remote inference service.

## Architecture

- `LocalAiOptions` binds `LocalAi` configuration.
- `OllamaProcessHostedService` can start `ollama serve` when the app starts.
- `OllamaModelHostedService` can optionally pull the configured model if missing.
- `IOllamaApi` wraps Ollama's local HTTP API.
- `ILocalChatClient` is the DI abstraction app code should use.
- `IAgentRuntime` runs configured local agents with a multi-iteration tool loop.
- `MarkdownAgentRegistry` merges configured agents with dynamic `.md` agent definitions in `data/agents`.
- `IAssistantOrchestrator` owns sessions, context estimation, and summarization near context limits.
- `IToolUsePlanner` uses a small router model to infer intent, response mode, effort, and direct-vs-orchestrated policy before the coordinator chooses any tools or subagents.
- `IModelRouter` chooses interactive, balanced, or deep-reasoning response models per request.
- `ILocalToolRegistry` exposes local tools to agents.
- `ILocalMemoryStore` stores local vector memory; the default implementation persists JSON under `data/memory`.
- `/`, `/sessions`, `/agents`, `/memory`, `/documents`, `/integrations`, `/models`, `/health`, and `/openai` expose the local app and API.

## Prerequisites

- macOS with Apple Silicon
- .NET 9 SDK
- Ollama installed with Homebrew

Install Ollama:

```bash
brew install --formula ollama
```

## Configuration

Defaults are in `src/LLLMax.Api/appsettings.json`:

```json
{
  "LocalAi": {
    "Provider": "Ollama",
    "BaseUrl": "http://127.0.0.1:11434",
    "DefaultModel": "gemma4:e2b",
    "RequireLoopback": true,
    "ManageProcess": true,
    "EnsureDefaultModel": false,
    "Memory": {
      "Enabled": true,
      "Provider": "File",
      "EmbeddingModel": "nomic-embed-text"
    }
  }
}
```

Important flags:

- `LocalAi:ManageProcess=true` starts `ollama serve` from the app if Ollama is not already running.
- `LocalAi:RequireLoopback=true` blocks non-local model endpoints.
- `LocalAi:EnsureDefaultModel=true` runs `ollama pull <DefaultModel>` at startup if the model is missing.
- `LocalAi:StopManagedProcessOnShutdown=true` stops only the process started by this app.
- `LocalAi:ModelRouter:RouterModel=gemma4:e2b` keeps routing on the fastest local model while preserving intent and parameter extraction.
- `LocalAi:ModelRouter:RouterMaxOutputTokens=160` caps planner output to reduce first-token latency.
- `LocalAi:Memory:EmbeddingModel=nomic-embed-text` uses a dedicated local embedding model for vector memory.
- `LocalAi:Tools:EnableSafeShell=false` keeps local shell execution disabled unless explicitly enabled.

To let the app start Ollama and pull the default model if missing:

```bash
LocalAi__ManageProcess=true LocalAi__EnsureDefaultModel=true dotnet run --project src/LLLMax.Api
```

To use an already-running Ollama service:

```bash
brew services start ollama
LocalAi__ManageProcess=false dotnet run --project src/LLLMax.Api
```

## Models

Pull a model manually if `EnsureDefaultModel` is false:

```bash
ollama pull gemma4:e2b
```

Pull the default embedding model before using memory:

```bash
ollama pull nomic-embed-text
```

Useful Gemma 4 model tags:

- `gemma4:e2b` - smallest local edge model, default for this repo
- `gemma4:e4b` - stronger local edge model
- `gemma4:26b` - MoE workstation model with about 4B active parameters
- `gemma4:31b` - flagship dense local model, about 20GB

Switch model with configuration:

```bash
LocalAi__DefaultModel=gemma4:31b dotnet run --project src/LLLMax.Api
```

## Run

```bash
dotnet run --project src/LLLMax.Api
```

The API listens on `http://localhost:5220` with the default launch profile.

For a restart-friendly one-liner that kills anything currently listening on port `5220` and starts LLLMax in the background:

```bash
scripts/lllmax
```

Optional shell alias:

```bash
alias lllmax="/Users/maxfalk/repos/gemma4-local/scripts/lllmax"
```

Override the default smoke-test model or port when needed:

```bash
LLLMAX_MODEL=gemma4:e2b LLLMAX_PORT=5221 scripts/lllmax
```

Open the built-in PWA chat interface:

```text
http://localhost:5220/
```

Open Swagger/API explorer:

```text
http://localhost:5220/swagger
```

## Docker

Run everything containerized: API, Ollama, and Qdrant.

```bash
docker compose up --build
```

This exposes:

- PWA: `http://localhost:5220/`
- API and Swagger: `http://localhost:5220/swagger`
- Ollama: `http://localhost:11434`
- Qdrant: `http://localhost:6333`

On macOS, Ollama may perform better as a native host service. Use this path to containerize the API and Qdrant while connecting to host Ollama:

```bash
brew services start ollama
docker compose -f docker-compose.host-ollama.yml up --build
```

The full Docker path uses `LocalAi:RequireLoopback=false` inside the Docker network because the API reaches Ollama through the `ollama` service name. Keep this deployment on a trusted local network unless you add authentication.

## First-Time Setup

The template tracks required/recommended models in `LocalAi:RequiredModels`.

Check setup status:

```bash
curl http://localhost:5220/setup
```

List configured model availability:

```bash
curl http://localhost:5220/models/available
```

List Ollama's OpenAI-compatible models endpoint:

```bash
curl http://localhost:5220/models/openai
```

Pull startup models manually through the API:

```bash
curl -X POST http://localhost:5220/setup/run
```

Pull one model through the API:

```bash
curl http://localhost:5220/setup/pull \
  -H "Content-Type: application/json" \
  -d '{"name":"gemma4:e4b"}'
```

If you want the app to wait until startup models are pulled before it reports startup complete:

```bash
LocalAi__BlockStartupUntilModelsReady=true dotnet run --project src/LLLMax.Api
```

## Call It

Health check:

```bash
curl http://localhost:5220/health
```

List locally pulled models:

```bash
curl http://localhost:5220/models
```

Chat with the default model:

```bash
curl http://localhost:5220/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"Give me three practical uses for a local LLM on a Mac."}'
```

Send multi-turn messages:

```bash
curl http://localhost:5220/chat \
  -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"system","content":"You are terse."},{"role":"user","content":"What is Ollama?"}]}'
```

Enable Gemma 4 thinking mode:

```bash
curl http://localhost:5220/chat \
  -H "Content-Type: application/json" \
  -d '{"enableThinking":true,"message":"Solve: if a train travels 90 km in 45 minutes, what is its speed in km/h?"}'
```

## Agents

Configured agents live under `LocalAi:Agents` in `appsettings.json`.

Default agents:

- `coordinator` - routes work and can delegate to `researcher` or `coder`
- `researcher` - summarizes local context and memory
- `coder` - gives implementation-oriented answers
- `builder` - can run allowlisted build commands when `safe_shell_command` is enabled
- `tester` - can run allowlisted test commands when `safe_shell_command` is enabled

List agents:

```bash
curl http://localhost:5220/agents
```

List tools:

```bash
curl http://localhost:5220/agents/tools
```

Run an agent:

```bash
curl http://localhost:5220/agents/run \
  -H "Content-Type: application/json" \
  -d '{"agent":"coordinator","message":"Ask the coder agent for a minimal approach to local vector memory."}'
```

Agent-to-agent communication is implemented as a local tool named `delegate_to_agent`. Each agent has explicit `AllowedAgents` and `AllowedTools` lists.

## Tool Calling

The baseline uses a simple JSON tool protocol instead of hiding behavior inside an SDK:

```json
{"tool":"memory_search","arguments":{"query":"local vector DB options","limit":3}}
```

Built-in tools:

- `delegate_to_agent` - local agent-to-agent delegation
- `memory_search` - search local vector memory
- `memory_write` - store local vector memory
- `safe_shell_command` - disabled-by-default allowlisted build/test command execution

This is intentionally simple and debuggable. Native OpenAI/Ollama tool calling can be added later behind `ILocalToolRegistry` or through `Microsoft.Extensions.AI`.

### Builder/Test Agents

The template includes a powerful but safe path for agents to verify local ideas:

```bash
LocalAi__Tools__EnableSafeShell=true dotnet run --project src/LLLMax.Api
```

Default allowlisted commands:

- `dotnet build`
- `dotnet test`
- `git status --short`
- `git diff --stat`

Ask the tester agent to run a build:

```bash
curl http://localhost:5220/agents/run \
  -H "Content-Type: application/json" \
  -d '{"agent":"tester","message":"Run dotnet build using safe_shell_command and summarize the result."}'
```

The shell tool only runs exact configured commands and only inside `LocalAi:Tools:ShellWorkingDirectory`. Keep it disabled unless you are intentionally running local builder agents.

## Memory

The memory architecture has two abstractions:

- `IEmbeddingGenerator` creates vectors locally.
- `ILocalMemoryStore` stores and searches vectors.

The default store is `QdrantVectorStore`, which persists vectorized memories in local Qdrant. `FileVectorStore` remains available with `LocalAi:Memory:Provider=File` for lightweight JSON persistence, and `InMemoryVectorStore` remains available with `LocalAi:Memory:Provider=InMemory` for experiments and tests.

Store memory:

```bash
curl http://localhost:5220/memory/upsert \
  -H "Content-Type: application/json" \
  -d '{"collection":"coordinator","text":"Qdrant is the preferred persistent local vector DB for this baseline."}'
```

Search memory:

```bash
curl http://localhost:5220/memory/search \
  -H "Content-Type: application/json" \
  -d '{"collection":"coordinator","query":"persistent vector database","limit":3}'
```

Hard-filter memory by exact metadata fields:

```bash
curl http://localhost:5220/memory/search \
  -H "Content-Type: application/json" \
  -d '{"collection":"documents","query":"termination liability","limit":5,"filter":{"category":"contracts","tenant":"personal"}}'
```

### Document Scoping

Use collections for coarse domains and metadata filters for hard boundaries. For example, contracts can live in the `documents` collection with `category=contracts`, or in a dedicated `contracts` collection if you prefer physical separation.

Vectorize a contracts folder with scope metadata:

```bash
curl http://localhost:5220/documents/vectorize-folder \
  -H "Content-Type: application/json" \
  -d '{"folderPath":"data/documents/contracts","collection":"documents","searchPattern":"*.txt","tenant":"personal","category":"contracts"}'
```

Every document chunk stores `kind=document_chunk`, `source`, `sourceFile`, `sourceRelativePath`, `sourceDirectory`, and any supplied `tenant`, `category`, or custom metadata. Retrieval filters are applied by the vector store, not by prompt instruction, so a contracts-only query cannot return non-contract chunks when `filter.category=contracts` is set.

### Background Jobs

Long-running work should be scheduled as background jobs so the main chat loop remains available. The built-in worker runs up to `LocalAi:Orchestration:MaxConcurrentBackgroundJobs` jobs at a time; the default is `3`.

Completed jobs can attach durable artifacts, such as generated markdown reports, under `data/background-job-artifacts`. The UI links completed job artifacts from the Background Jobs panel.

Current background-capable task:

- `document_vectorize_folder`: vectorize large local document folders into Qdrant with progress, cancellation, and session completion notifications.
- `memory_report`: retrieve scoped Qdrant/local memory chunks and generate a markdown report with the local model.

Document vectorization computes SHA-256 content hashes and skips files already present in the target collection. Chunks are upserted in batches to reduce Qdrant write overhead.

Good future background job candidates:

- batch OCR and invoice extraction
- deep web research with multiple browsed sources
- local build/test/benchmark runs
- large API synchronization from explicitly registered integrations
- memory consolidation and re-embedding migrations
- report generation over many retrieved documents
- local artifact downloads from approved URLs with checksum verification

Schedule a document vectorization job:

```bash
curl http://localhost:5220/background-jobs \
  -H "Content-Type: application/json" \
  -d '{"kind":"document_vectorize_folder","title":"Vectorize contracts","payload":{"folderPath":"data/documents/contracts","collection":"documents","tenant":"personal","category":"contracts"}}'
```

Inspect active jobs:

```bash
curl http://localhost:5220/background-jobs
```

Schedule a report over scoped memory:

```bash
curl http://localhost:5220/background-jobs \
  -H "Content-Type: application/json" \
  -d '{"kind":"memory_report","title":"Contract risk report","payload":{"collection":"documents","query":"renewal indemnity liability","filter":{"tenant":"personal","category":"contracts"},"instruction":"Summarize renewal windows and liability caps with source filenames."}}'
```

### Embeddings

Use a dedicated embedding model rather than a chat model. Recommended local default:

```bash
ollama pull nomic-embed-text
```

Smaller chat models can orchestrate memory tool calls, but embeddings should come from an embedding model. This gives stable vector dimensions and better retrieval quality.

### Persistent Vector Store

Qdrant is the best fit for this baseline because it is mature, simple, local-first, has an HTTP API, and runs cleanly in Docker.

Start Qdrant:

```bash
docker compose up -d qdrant
```

Qdrant endpoints:

- HTTP: `http://127.0.0.1:6333`
- gRPC: `http://127.0.0.1:6334`

Qdrant is the default persistent vector store. Use `LocalAi:Memory:Provider=File` only when you explicitly want file-backed memory without a database.

## OpenAI Compatibility

Ollama exposes OpenAI-compatible local endpoints at:

```text
http://127.0.0.1:11434/v1/
```

Use API key `ollama`; Ollama requires a value but ignores it.

Supported by Ollama's OpenAI-compatible API:

- `/v1/chat/completions`
- `/v1/responses`
- `/v1/models`
- `/v1/embeddings`
- Tool/function calling on compatible local models
- Vision on compatible local models

Not every model is equally good at tool calling. Gemma 4 advertises native function-calling support, but behavior should be tested per model and quantization.

Latest Gemma family models in Ollama, including Gemma 4, support tool/function-calling semantics through Ollama's APIs. Treat support as model-capability plus runtime-contract: the API supports tools, the model is trained for tool use, but reliability still needs application-level validation.

Example OpenAI-compatible local call:

```bash
curl http://127.0.0.1:11434/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"gemma4:e2b","messages":[{"role":"user","content":"Say hello from local Ollama."}]}'
```

## NuGet Options

Recommended baseline choices:

- No mandatory AI SDK package yet; this repo uses `HttpClient` so the protocol is transparent and local-only guardrails are explicit.
- `OllamaSharp` is the most mature .NET Ollama client and supports `IChatClient` from `Microsoft.Extensions.AI`.
- `Microsoft.Extensions.AI` is useful if you want provider-neutral abstractions and tool calling patterns.
- `Microsoft.Extensions.AI.OpenAI` plus the official `OpenAI` package can target Ollama's local `/v1` OpenAI-compatible endpoint.
- `OllamaSharp.ModelContextProtocol` is useful when you want MCP tools wired into Ollama-based agents.
- `OllamaNet.Client` is another Ollama client with streaming, embeddings, model management, and tool-calling support.
- `Qdrant.Client` is the natural .NET client if you implement persistent vector memory against Qdrant.
- `Microsoft.SemanticKernel` is useful if you want a larger agent framework, but it is heavier than this baseline.

For this baseline, keep model startup/process ownership in your own hosted services even if you later add an SDK package. SDKs usually call an existing endpoint; they do not replace the need for local process lifecycle and local-only validation.

## Template Usage

Install this repo as a local .NET template:

```bash
dotnet new install /Users/maxfalk/repos/LLLMax
```

Create a new experiment repo from it:

```bash
mkdir /Users/maxfalk/repos/my-local-ai-experiment
cd /Users/maxfalk/repos/my-local-ai-experiment
dotnet new local-ai --name MyLocalAiExperiment --DefaultModel gemma4:e2b
```

Uninstall the template:

```bash
dotnet new uninstall /Users/maxfalk/repos/LLLMax
```

## Frontier App Playbook

See `docs/FRONTIER_LOCAL_APPS.md` for agent/tool/memory patterns, including how to evolve this into agents that can build and test local ideas with safe allowlisted tools.

## Local-Only Rules

- Keep `LocalAi:RequireLoopback=true` unless you intentionally change the architecture.
- Use `http://127.0.0.1:11434` or `http://localhost:11434` for Ollama.
- Avoid `gemma4:*cloud` tags if the goal is local-only inference.
- Do not configure OpenAI/Azure/OpenRouter/Gemini API keys in this baseline.
