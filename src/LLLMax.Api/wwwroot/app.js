const state = {
  sessionId: null,
  uploadedDocumentId: null,
  models: [],
  sessions: [],
  tools: [],
  skills: [],
  agents: [],
  taskGraph: null,
  backgroundJobs: [],
  memoryCollections: [],
  memoryStats: null,
  memoryGraph: null,
  memoryGraphLayer: 'all',
  memoryGraphQuery: '',
  memoryGraphZoom: 1,
  memoryGraphPanX: 0,
  memoryGraphPanY: 0,
  memoryGraphFocusId: null,
  memoryGraphFocusLabel: null,
  memoryGraphDrillLevel: 0,
  memoryGraphOnlyRelated: false,
  memoryGraphTypes: [],
  memoryGraphFilter: null,
  memoryGraphScope: 'total',
  memoryReviewOpen: false,
  memoryReview: null,
  foundationProfile: null,
  selectedCollection: null,
  memoryProfile: null,
  draftSession: null,
  visibleSessionCount: 12,
  messageTraces: new Map(),
  selectedTraceId: null,
  consolidationJobs: [],
  approvals: [],
  mcpServers: [],
  patchProposals: [],
  currentMessages: [],
  isStreaming: false,
  activeAssistantId: null,
  editingAgent: null,
  profileMode: 'edit',
  profileStep: 0,
  inspectEvents: [],
  inspectCollapsed: true,
  inspectDismissed: false,
  leftPanelCollapsed: true,
  memoryGraphSearchOpen: false
};

let knownJobStates = new Map();

const $ = id => document.getElementById(id);
const effortMap = ['low', 'medium', 'high'];

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: options.body instanceof FormData ? undefined : { 'Content-Type': 'application/json' },
    ...options
  });

  if (!response.ok) {
    throw new Error(await response.text());
  }

  if (response.status === 204) {
    return null;
  }

  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

async function guarded(action, label = 'working', options = {}) {
  setStatus(label, 'busy');
  setWorking(options.overlay !== false, label);
  try {
    const result = await action();
    setStatus('Ready', 'ok');
    return result;
  } catch (error) {
    setStatus(error.message, 'error');
    throw error;
  } finally {
    setWorking(false);
  }
}

function setStatus(text, kind = 'ok') {
  $('statusLine').textContent = text;
  $('statusLine').dataset.kind = kind;
}

function setWorking(isWorking, label = '') {
  $('workOverlay').hidden = !isWorking;
  $('workAssistantName').textContent = `${assistantDisplayName()} is working`;
  $('workLabel').textContent = label;
}

function assistantDisplayName() {
  return state.foundationProfile?.assistantName?.trim() || 'LLLMax';
}

function assistantDisplayDescription() {
  return state.foundationProfile?.assistantDescription?.trim() || 'Frontier local assistant';
}

function renderAssistantIdentity() {
  const name = assistantDisplayName();
  $('assistantBrandName').textContent = name;
  $('assistantBrandDescription').textContent = assistantDisplayDescription();
  $('workAssistantName').textContent = `${name} is working`;
  $('prompt').placeholder = `Ask ${name} to research, route, extract, summarize, or use a scoped tool...`;
  document.title = name;
}

function setLeftPanelCollapsed(collapsed) {
  state.leftPanelCollapsed = collapsed;
  $('appShell')?.classList.toggle('left-collapsed', collapsed);
  $('toggleLeftPanel')?.classList.toggle('active', !collapsed);
  $('toggleLeftPanel')?.setAttribute('aria-label', collapsed ? 'Open sessions' : 'Close sessions');
}

function renderMessages(messages) {
  clearInlineProgress();
  const previousTraceId = state.selectedTraceId;
  state.currentMessages = messages.filter(message => message.role !== 'progress');
  state.messageTraces = new Map();
  $('messages').innerHTML = state.currentMessages.map((message, index) => renderMessage(message, index)).join('');
  document.querySelectorAll('[data-trace-id]').forEach(element => {
    element.addEventListener('click', () => selectMessageTrace(element.dataset.traceId));
  });

  if (previousTraceId && state.messageTraces.has(previousTraceId)) {
    selectMessageTrace(previousTraceId);
  } else {
    clearResponseDetails();
  }

  $('messages').scrollTop = $('messages').scrollHeight;
}

function renderMessage(message, index) {
  const role = message.role === 'user' ? 'user' : 'assistant';
  const raw = message.content ?? '';
  const draftAttribute = message.draftId ? ` data-draft-id="${escapeHtml(message.draftId)}"` : '';

  if (role === 'user') {
    return `<div class="message user" data-raw="${escapeHtml(raw)}">${escapeHtml(raw)}</div>`;
  }

  const hasTrace = Boolean(message.reasoningSteps?.length || message.taskGraph || message.toolTraces?.length || message.citations?.length);

  if (!hasTrace) {
    return `<div class="message assistant" data-raw="${escapeHtml(raw)}"${draftAttribute}><div class="markdown-body">${renderMarkdown(raw)}</div></div>`;
  }

  const traceId = message.traceId ?? `message-${index}`;
  state.messageTraces.set(traceId, {
    message,
    graph: message.taskGraph ?? null,
    reasoningSteps: message.reasoningSteps ?? [],
    toolTraces: message.toolTraces ?? [],
    citations: message.citations ?? []
  });

  return `<div class="message assistant ${hasTrace ? 'has-trace' : ''} ${traceId === state.selectedTraceId ? 'selected' : ''}" data-raw="${escapeHtml(raw)}" data-trace-id="${escapeHtml(traceId)}"${draftAttribute}><div class="markdown-body">${renderMarkdown(raw)}</div>${renderMessageCitations(message.citations ?? [])}${hasTrace ? '<span class="trace-hint">trace</span>' : ''}</div>`;
}

function renderMessageCitations(citations) {
  const visible = visibleCitations(citations);
  return visible.length
    ? `<div class="message-citations"><span>Sources</span>${visible.map(citation => citation.url
      ? `<a href="${escapeHtml(citation.url)}" target="_blank" rel="noreferrer">${escapeHtml(citationLabel(citation))}</a>`
      : `<code>${escapeHtml(citationLabel(citation))}</code>`).join('')}</div>`
    : '';
}

function renderRuntime(health) {
  const highModel = state.models.find(model => model.name === 'gemma4:31b');
  const coordinator = state.coordinatorModel?.currentCoordinatorModel ?? 'unknown';
  $('runtime').innerHTML = `
    <div class="runtime-pill ${health?.isHealthy ? 'healthy' : 'unknown'}"><span>ollama</span><strong>${health?.isHealthy ? 'healthy' : 'unknown'}</strong></div>
    <div class="runtime-pill"><span>models</span><strong>${escapeHtml(String(state.models.length))}</strong></div>
    <div class="runtime-pill"><span>coordinator</span><strong>${escapeHtml(coordinator)}</strong></div>
    <div class="runtime-pill"><span>31b</span><strong>${escapeHtml(highModel ? `${highModel.sizeGb} GB` : 'missing')}</strong></div>
    <div class="runtime-pill ${state.sessionId ? 'healthy' : ''}"><span>session</span><strong>${state.sessionId ? 'active' : 'none'}</strong></div>
  `;
}

function renderMetrics(metrics) {
  if (!metrics) {
    $('metrics').innerHTML = '<p class="muted">No run yet.</p>';
    return;
  }

  const items = [
    ['model', metrics.model],
    ['reasoning', metrics.reasoningEffort],
    ['tokens/s', metrics.tokensPerSecond ?? '-'],
    ['eval tokens', metrics.evalCount ?? '-'],
    ['prompt tokens', metrics.promptEvalCount ?? '-'],
    ['context est.', metrics.estimatedContextTokens ?? '-']
  ];

  $('metrics').innerHTML = items.map(([label, value]) => `
    <div class="metric"><strong>${escapeHtml(String(value))}</strong><span>${label}</span></div>
  `).join('');
}

function renderSteps(steps) {
  $('steps').innerHTML = steps?.length
    ? steps.map(step => `<div class="step ${isToolStep(step) ? 'tool-step' : ''}"><strong>${escapeHtml(step.kind)}</strong>\n${escapeHtml(step.content)}</div>`).join('')
    : '<p class="muted">Select an assistant message to inspect its reasoning.</p>';
}

function isToolStep(step) {
  return String(step?.kind ?? '').includes('tool') || String(step?.content ?? '').includes('<tool_call|>');
}

function selectMessageTrace(traceId) {
  state.selectedTraceId = traceId;
  document.querySelectorAll('[data-trace-id]').forEach(element => {
    element.classList.toggle('selected', element.dataset.traceId === traceId);
  });

  const trace = state.messageTraces.get(traceId);
  renderTaskGraph(trace?.graph ?? null, trace ? 'No task graph was recorded for this message.' : 'Select an assistant message to inspect its task graph.');
  renderToolTraces(trace?.toolTraces ?? []);
  renderCitations(trace?.citations ?? []);
  renderSteps(trace?.reasoningSteps ?? []);
}

function clearResponseDetails() {
  state.selectedTraceId = null;
  document.querySelectorAll('[data-trace-id]').forEach(element => element.classList.remove('selected'));
  renderMetrics(null);
  renderTaskGraph(null, 'Select an assistant message to inspect its task graph.');
  renderToolTraces([]);
  renderCitations([]);
  renderSteps([]);
}

function resetInspectPanel() {
  state.inspectEvents = [];
  state.inspectCollapsed = true;
  state.inspectDismissed = false;
  renderInspectPanel();
}

function appendInspectEvent(event) {
  if (state.inspectDismissed) return;

  const item = inspectEventFromStream(event);
  if (!item) return;

  if (item.mergeKey) {
    const nextEvents = [...state.inspectEvents];
    const index = nextEvents.findIndex(existing => existing.mergeKey === item.mergeKey);

    if (index >= 0) {
      nextEvents[index] = {
        ...nextEvents[index],
        ...item,
        content: truncateEnd((nextEvents[index].content ?? '') + (item.content ?? ''), 900),
        tick: (nextEvents[index].tick ?? 0) + 1
      };
      state.inspectEvents = nextEvents.slice(-24);
      renderInspectPanel();
      return;
    }
  }

  state.inspectEvents = [...state.inspectEvents, { ...item, tick: Date.now() }].slice(-24);
  renderInspectPanel();
}

function inspectEventFromStream(event) {
  if (event.type === 'chunk' || event.type === 'task_graph') return null;
  const runtimeEvent = event.payload?.runtimeEvent;
  const kind = runtimeEvent?.kind ?? event.type;
  const tool = runtimeEvent?.tool;
  const result = runtimeEvent?.result;
  const args = runtimeEvent?.arguments ?? {};
  const target = toolTraceTarget(tool, args);
  const isDelta = kind === 'model_delta';
  const preview = result ? toolResultPreview(result) : event.content;

  return {
    time: new Date().toLocaleTimeString(),
    kind,
    label: progressLabel(kind, tool),
    status: progressStatus(kind),
    target,
    content: isDelta ? preview : truncateMiddle(preview, 520),
    mergeKey: isDelta ? `${args.agent ?? tool ?? 'model'}:delta` : null
  };
}

function renderInspectPanel() {
  let panel = $('inspectPanel');

  if (state.inspectDismissed || state.inspectEvents.length === 0) {
    panel?.remove();
    return;
  }

  if (!panel) {
    panel = document.createElement('section');
    panel.id = 'inspectPanel';
    panel.className = 'inspect-panel';
    $('messages').appendChild(panel);
  }

  panel.classList.toggle('collapsed', state.inspectCollapsed);
  const latest = state.inspectEvents.at(-1);
  const latestText = inspectCollapsedText(latest);
  panel.innerHTML = `
    <div class="inspect-head">
      <span><strong>Live Inspect</strong><small>${escapeHtml(latest?.label ?? 'waiting')} · ${escapeHtml(latest?.status ?? 'active')}</small></span>
      <div class="inspect-actions">
        <button id="toggleInspect" type="button">${state.inspectCollapsed ? 'Expand' : 'Minimize'}</button>
        <button id="closeInspect" type="button">Close</button>
      </div>
    </div>
    ${state.inspectCollapsed
      ? `<div class="inspect-mini"><span class="inspect-mini-dot"></span><span class="inspect-mini-text" data-tick="${escapeHtml(String(latest?.tick ?? '0'))}">${escapeHtml(latestText)}</span></div>`
      : `<div class="inspect-log">${state.inspectEvents.map(renderInspectLogItem).join('')}</div>`}
  `;
  $('toggleInspect')?.addEventListener('click', () => {
    state.inspectCollapsed = !state.inspectCollapsed;
    renderInspectPanel();
  });
  $('closeInspect')?.addEventListener('click', () => {
    state.inspectDismissed = true;
    renderInspectPanel();
  });
  const log = panel.querySelector('.inspect-log');
  if (log) log.scrollTop = log.scrollHeight;
  requestAnimationFrame(() => {
    $('messages').scrollTop = $('messages').scrollHeight;
  });
}

function inspectCollapsedText(item) {
  if (!item) return 'Waiting for activity...';
  const pieces = [item.target, item.content].filter(Boolean).join(' · ');
  return truncateEnd(pieces || `${item.label} ${item.status}`, 180);
}

function renderInspectLogItem(item) {
  return `
    <div class="inspect-item ${escapeHtml(item.status)}">
      <span class="inspect-time">${escapeHtml(item.time)}</span>
      <span class="inspect-label">${escapeHtml(item.label)}</span>
      <span class="inspect-content">${item.target ? `<code>${escapeHtml(item.target)}</code> ` : ''}${escapeHtml(item.content ?? '')}</span>
    </div>
  `;
}

function renderToolTraces(toolTraces) {
  $('toolTraces').innerHTML = toolTraces?.length
    ? toolTraces.map(renderToolTraceCard).join('')
    : '<p class="muted">No tool calls recorded for this message.</p>';
}

function renderToolTraceCard(trace) {
  const status = trace.status ?? 'unknown';
  const args = trace.arguments ?? {};
  const target = toolTraceTarget(trace.tool, args);
  const preview = trace.error || toolResultPreview(trace.result);
  const duration = trace.durationMs ? `${Math.round(trace.durationMs)}ms` : null;
  const meta = [status, duration, target].filter(Boolean).join(' · ');
  const rawBlocks = [
    trace.arguments ? ['Arguments', JSON.stringify(trace.arguments, null, 2)] : null,
    trace.result ? ['Result', trace.result] : null,
    trace.error ? ['Error', trace.error] : null
  ].filter(Boolean);

  return `
    <details class="tool-trace ${escapeHtml(status)}">
      <summary>
        <span class="tool-trace-main"><strong>${escapeHtml(friendlyToolName(trace.tool))}</strong><small>${escapeHtml(meta || 'tool call')}</small></span>
        <span class="status-pill ${escapeHtml(status)}">${escapeHtml(status)}</span>
      </summary>
      ${preview ? `<p class="tool-preview">${escapeHtml(preview)}</p>` : '<p class="tool-preview muted">No result preview.</p>'}
      ${rawBlocks.length ? `<details class="raw-detail"><summary>Raw details</summary>${rawBlocks.map(([label, value]) => `<strong>${escapeHtml(label)}</strong><pre>${escapeHtml(value)}</pre>`).join('')}</details>` : ''}
    </details>
  `;
}

function friendlyToolName(tool) {
  return String(tool ?? 'tool').replaceAll('_', ' ');
}

function toolTraceTarget(tool, args = {}) {
  if (tool === 'web_browse' && args.url) return displayUrl(args.url);
  if (tool === 'memory_search' && args.query) return `"${args.query}"`;
  if (tool === 'delegate_to_agent' && args.agent) return args.agent;
  if (tool === 'schedule_background_job' && args.kind) return args.kind;
  if (args.path) return args.path;
  if (args.folderPath) return args.folderPath;
  return null;
}

function toolResultPreview(result) {
  if (!result) return '';
  const cleaned = String(result)
    .replace(/^URL:\s*(\S+)\s*/i, 'Fetched $1. ')
    .replace(/\s+/g, ' ')
    .trim();
  return truncateMiddle(cleaned, 460);
}

function displayUrl(value) {
  try {
    const url = new URL(value);
    return `${url.hostname.replace(/^www\./, '')}${url.pathname === '/' ? '' : url.pathname}`;
  } catch {
    return String(value ?? '');
  }
}

function truncateMiddle(value, maxLength = 220) {
  const text = String(value ?? '');
  if (text.length <= maxLength) return text;
  const head = Math.ceil((maxLength - 1) * 0.68);
  const tail = Math.floor((maxLength - 1) * 0.32);
  return `${text.slice(0, head)}…${text.slice(-tail)}`;
}

function truncateEnd(value, maxLength = 220) {
  const text = String(value ?? '').replace(/\s+/g, ' ').trim();
  return text.length <= maxLength ? text : `${text.slice(0, maxLength - 1)}…`;
}

function renderCitations(citations) {
  const visible = visibleCitations(citations);
  $('citations').innerHTML = visible.length
    ? visible.map(citation => {
      const title = citation.url
        ? `<a href="${escapeHtml(citation.url)}" target="_blank" rel="noreferrer">${escapeHtml(citationLabel(citation))}</a>`
        : escapeHtml(citationLabel(citation));
      const detail = [citation.kind, citation.source, citation.chunk ? `chunk ${citation.chunk}` : null, citation.score ? `score ${Number(citation.score).toFixed(3)}` : null]
        .filter(Boolean)
        .join(' · ');
      return `<div class="citation"><strong>${title}</strong><span>${escapeHtml(detail)}</span></div>`;
    }).join('')
    : '<p class="muted">No citations recorded for this message.</p>';
}

function visibleCitations(citations = []) {
  const seen = new Set();
  return citations
    .filter(citation => citation?.url || citation?.source || citation?.kind === 'web' || !isGuidLike(citation?.title))
    .filter(citation => {
      const key = `${citation.kind}|${citation.url ?? ''}|${citation.source ?? ''}|${citation.chunk ?? ''}|${citationLabel(citation)}`;
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    });
}

function citationLabel(citation) {
  if (citation.url) {
    try {
      return new URL(citation.url).hostname.replace(/^www\./, '');
    } catch {
      return citation.title || citation.url;
    }
  }

  if (citation.title && !isGuidLike(citation.title)) return citation.title;
  if (citation.source && !isGuidLike(citation.source)) return citation.source;
  return citation.kind === 'memory' ? 'Local memory' : 'Source';
}

function isGuidLike(value) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(String(value ?? ''));
}

async function loadModels() {
  state.models = await api('/models');
  state.coordinatorModel = await api('/models/coordinator');
  $('modelSelect').innerHTML = '<option value="">Coordinator default</option>' + state.models.map(model => `
    <option value="${escapeHtml(model.name)}">${escapeHtml(model.name)}</option>
  `).join('');
  $('modelSelect').value = state.coordinatorModel?.coordinatorOverride ?? '';
}

async function setCoordinatorModel(model) {
  state.coordinatorModel = await api('/models/coordinator', {
    method: 'PUT',
    body: JSON.stringify({ model: model || null })
  });
  renderRuntime(await api('/health'));
}

async function loadAgents() {
  state.agents = await api('/agents');
  $('agents').innerHTML = state.agents.map(agent => `
    <button data-edit-agent-prompt="${escapeHtml(agent.name)}" type="button" title="${escapeHtml(agent.description)}">${escapeHtml(agent.name)}<br><small>${agent.allowedTools.length} tools · edit prompt</small></button>
  `).join('');

  document.querySelectorAll('[data-edit-agent-prompt]').forEach(button => {
    button.addEventListener('click', () => openAgentPrompt(button.dataset.editAgentPrompt));
  });
}

async function openAgentPrompt(name) {
  await guarded(async () => {
    const prompt = await api(`/agents/${encodeURIComponent(name)}/prompt`);
    state.editingAgent = prompt;
    $('agentPromptTitle').textContent = `${prompt.name} system prompt`;
    $('agentPromptText').value = prompt.effectiveSystemPrompt ?? '';
    $('agentBasePrompt').textContent = prompt.baseSystemPrompt ?? '';
    $('resetAgentPrompt').disabled = !prompt.hasOverride;
    $('agentPromptModal').hidden = false;
  }, 'Opening agent prompt...', { overlay: false });
}

function closeAgentPromptModal() {
  $('agentPromptModal').hidden = true;
  state.editingAgent = null;
}

async function saveAgentPrompt() {
  if (!state.editingAgent) return;

  await guarded(async () => {
    const result = await api(`/agents/${encodeURIComponent(state.editingAgent.name)}/prompt`, {
      method: 'PUT',
      body: JSON.stringify({ systemPrompt: $('agentPromptText').value })
    });
    state.editingAgent = result;
    closeAgentPromptModal();
    await loadAgents();
  }, 'Saving agent prompt...');
}

async function resetAgentPrompt() {
  if (!state.editingAgent || !confirm(`Reset ${state.editingAgent.name} to its base prompt?`)) return;

  await guarded(async () => {
    const result = await api(`/agents/${encodeURIComponent(state.editingAgent.name)}/prompt`, { method: 'DELETE' });
    state.editingAgent = result;
    $('agentPromptText').value = result.effectiveSystemPrompt ?? '';
    $('resetAgentPrompt').disabled = true;
    await loadAgents();
  }, 'Resetting agent prompt...');
}

async function loadTools() {
  state.tools = await api('/agents/tools');
  $('tools').innerHTML = state.tools.map(tool => `
    <details>
      <summary>${escapeHtml(tool.name)}</summary>
      <p>${escapeHtml(tool.description)}</p>
      <code>${escapeHtml(JSON.parse(tool.argumentsJsonSchema).title ?? 'schema')}</code>
    </details>
  `).join('');
}

async function loadSkills() {
  state.skills = await api('/skills');
  $('skills').innerHTML = state.skills.map(skill => `
    <details>
      <summary>${escapeHtml(skill.name)} <small>${escapeHtml(String(skill.priority))}</small></summary>
      <p>${escapeHtml(skill.description)}</p>
      <code>${escapeHtml((skill.triggers ?? []).slice(0, 5).join(', ') || 'no triggers')}</code>
    </details>
  `).join('') || '<p class="muted">No skills found.</p>';
}

async function loadApprovals() {
  state.approvals = await api('/approvals');
  const pending = state.approvals.filter(approval => approval.status === 'pending');
  const recent = [...pending, ...state.approvals.filter(approval => approval.status !== 'pending')].slice(0, 5);
  $('approvals').innerHTML = recent.map(renderApproval).join('') || '<p class="muted">No approval requests.</p>';

  document.querySelectorAll('[data-approve-once]').forEach(button => button.addEventListener('click', () => decideApproval(button.dataset.approveOnce, true, 'once')));
  document.querySelectorAll('[data-approve-session]').forEach(button => button.addEventListener('click', () => decideApproval(button.dataset.approveSession, true, 'session')));
  document.querySelectorAll('[data-approve-persist]').forEach(button => button.addEventListener('click', () => decideApproval(button.dataset.approvePersist, true, 'persistent')));
  document.querySelectorAll('[data-reject]').forEach(button => button.addEventListener('click', () => decideApproval(button.dataset.reject, false)));
}

async function loadPatchProposals() {
  state.patchProposals = await api('/self-improvement/patch-proposals');
  $('patchProposals').innerHTML = state.patchProposals.slice(0, 8).map(proposal => `
    <button class="patch-proposal-button" data-open-patch-proposal="${escapeHtml(proposal.id)}" type="button">
      <strong>${escapeHtml(proposal.fileName)}</strong>
      <span>${escapeHtml(new Date(proposal.createdAt).toLocaleString())} · ${escapeHtml(proposal.bytes)} bytes</span>
    </button>
  `).join('') || '<p class="muted">No saved patch proposals.</p>';

  document.querySelectorAll('[data-open-patch-proposal]').forEach(button => {
    button.addEventListener('click', () => openPatchProposal(button.dataset.openPatchProposal));
  });
}

async function openPatchProposal(id) {
  await guarded(async () => {
    const proposal = await api(`/self-improvement/patch-proposals/${encodeURIComponent(id)}`);
    const container = $('patchProposals');
    container.innerHTML = `
      <button id="backToPatchProposals" type="button">Back to proposals</button>
      <article class="approval-payload patch-proposal saved-patch" open>
        <strong>${escapeHtml(proposal.fileName)}</strong>
        <div class="patch-preview">${renderPatchDiff(extractDiffContent(proposal.content))}</div>
      </article>
    `;
    $('backToPatchProposals').addEventListener('click', loadPatchProposals);
  }, 'Opening patch proposal...', { overlay: false });
}

function extractDiffContent(content) {
  const match = String(content ?? '').match(/```diff\n([\s\S]*?)\n```/);
  return match ? match[1] : content;
}

function renderApproval(approval) {
  return `
    <div class="approval ${escapeHtml(approval.status)}">
      <strong>${escapeHtml(approval.title)}</strong>
      <span>${escapeHtml(approval.status)} · ${escapeHtml(approval.kind)}${approval.scope ? ` · ${escapeHtml(approval.scope)}` : ''}</span>
      <p>${escapeHtml(approval.description)}</p>
      ${renderApprovalPayload(approval)}
      ${approval.status === 'pending' ? `<div class="button-row"><button data-approve-once="${escapeHtml(approval.id)}" type="button">Allow once</button><button data-approve-session="${escapeHtml(approval.id)}" type="button">Allow session</button><button data-approve-persist="${escapeHtml(approval.id)}" type="button">Persist</button><button data-reject="${escapeHtml(approval.id)}" type="button">Reject</button></div>` : ''}
    </div>
  `;
}

function renderApprovalPayload(approval) {
  if (approval.kind === 'propose_patch') {
    const payload = approval.payload ?? {};
    const title = payload.title ?? payload.Title ?? 'Patch proposal';
    const rationale = payload.rationale ?? payload.Rationale ?? '';
    const patch = payload.patch ?? payload.Patch ?? '';
    return `
      <details class="approval-payload patch-proposal" open>
        <summary>Review proposed patch</summary>
        <strong>${escapeHtml(title)}</strong>
        <p>${escapeHtml(rationale)}</p>
        <div class="patch-preview">${renderPatchDiff(patch)}</div>
        <p class="approval-hint">Approve to let the agent save this as a local patch proposal artifact, or reject and reply with feedback for another proposal.</p>
      </details>
    `;
  }

  if (approval.kind === 'workspace_write') {
    const payload = approval.payload ?? {};
    return `
      <details class="approval-payload">
        <summary>Review file write</summary>
        <strong>${escapeHtml(payload.path ?? payload.Path ?? 'workspace file')}</strong>
        <pre>${escapeHtml(payload.content ?? payload.Content ?? '')}</pre>
      </details>
    `;
  }

  return '';
}

function renderPatchDiff(patch) {
  return String(patch).split('\n').map(line => {
    const kind = line.startsWith('+') && !line.startsWith('+++') ? 'add'
      : line.startsWith('-') && !line.startsWith('---') ? 'remove'
      : line.startsWith('@@') || line.startsWith('***') || line.startsWith('diff --git') ? 'meta'
      : 'context';
    return `<div class="diff-line ${kind}">${escapeHtml(line || ' ')}</div>`;
  }).join('');
}

async function loadMcpServers() {
  state.mcpServers = await api('/mcp/servers');
  $('mcpServers').innerHTML = state.mcpServers.map(server => `
    <details>
      <summary>${escapeHtml(server.name)} <small>${escapeHtml(server.transport)} · ${escapeHtml(server.status)}</small></summary>
      ${(server.tools ?? []).map(tool => `
        <div class="mcp-tool ${escapeHtml(tool.approvalStatus)}">
          <strong>${escapeHtml(tool.name)}</strong>
          <span>${escapeHtml(tool.approvalStatus)}</span>
          <div class="button-row"><button data-mcp-approve="${escapeHtml(server.id)}:${escapeHtml(tool.name)}" type="button">Approve tool</button><button data-mcp-reject="${escapeHtml(server.id)}:${escapeHtml(tool.name)}" type="button">Reject tool</button></div>
        </div>
      `).join('')}
    </details>
  `).join('') || '<p class="muted">No MCP servers registered.</p>';

  document.querySelectorAll('[data-mcp-approve]').forEach(button => button.addEventListener('click', () => decideMcpTool(button.dataset.mcpApprove, true)));
  document.querySelectorAll('[data-mcp-reject]').forEach(button => button.addEventListener('click', () => decideMcpTool(button.dataset.mcpReject, false)));
}

async function loadSessions() {
  state.sessions = (await api('/sessions')).filter(session => session.messageCount > 0);
  const visible = state.sessions.slice(0, state.visibleSessionCount);
  const groups = groupSessionsByDate(visible);
  $('sessions').innerHTML = groups.map(group => `
    <section class="session-group">
      <strong>${escapeHtml(group.label)}</strong>
      ${group.sessions.map(session => `
        <div class="session-row ${session.id === state.sessionId ? 'active' : ''}">
          <button type="button" data-session="${session.id}">
            ${escapeHtml(session.title)}<br><small>${escapeHtml(formatSessionDate(session.updatedAt))} · ${session.messageCount} messages</small>
          </button>
          <button class="session-delete" data-delete-session="${session.id}" type="button" title="Delete session" aria-label="Delete session">x</button>
        </div>
      `).join('')}
    </section>
  `).join('') || '<p class="muted">No saved sessions yet.</p>';

  if (state.sessions.length > visible.length) {
    $('sessions').innerHTML += `<button id="showMoreSessions" type="button">Show ${escapeHtml(Math.min(12, state.sessions.length - visible.length))} more</button>`;
  }

  document.querySelectorAll('[data-session]').forEach(button => {
    button.addEventListener('click', () => guarded(() => openSession(button.dataset.session), 'Opening session...'));
  });
  document.querySelectorAll('[data-delete-session]').forEach(button => {
    button.addEventListener('click', event => {
      event.stopPropagation();
      deleteSession(button.dataset.deleteSession);
    });
  });
  $('showMoreSessions')?.addEventListener('click', () => {
    state.visibleSessionCount += 12;
    loadSessions();
  });
}

function groupSessionsByDate(sessions) {
  const today = new Date().toDateString();
  const yesterday = new Date(Date.now() - 86400000).toDateString();
  const groups = new Map();

  for (const session of sessions) {
    const date = new Date(session.updatedAt);
    const key = date.toDateString() === today ? 'Today' : date.toDateString() === yesterday ? 'Yesterday' : date.toLocaleDateString();

    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(session);
  }

  return [...groups.entries()].map(([label, groupSessions]) => ({ label, sessions: groupSessions }));
}

function formatSessionDate(value) {
  return new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

async function loadMemoryStats() {
  const stats = await api('/memory/stats');
  const foundationProfile = await api('/user-profile/');
  const profile = await api('/memory/profile');
  state.memoryStats = stats;
  state.foundationProfile = foundationProfile;
  state.memoryProfile = profile;
  $('memoryStats').innerHTML = `
    <div class="runtime-pill"><span>provider</span><strong>${escapeHtml(stats.provider)}</strong></div>
    <div class="runtime-pill"><span>collections</span><strong>${stats.collectionCount}</strong></div>
    <div class="runtime-pill"><span>records</span><strong>${stats.recordCount}</strong></div>
  `;
  renderFoundationProfile(foundationProfile, profile);
  renderMemoryDashboard(stats, profile);
  await loadMemoryGraph({ renderOnlyIfVisible: true });
  await loadMemoryCollections();
}

function renderFoundationProfile(profile, memoryProfile) {
  if (!$('foundationProfileForm')) return;

  $('profileAssistantName').value = profile?.assistantName ?? '';
  $('profileAssistantDescription').value = profile?.assistantDescription ?? '';
  $('profileUsername').value = profile?.username ?? '';
  $('profileEmail').value = profile?.email ?? '';
  $('profileFullName').value = profile?.fullName ?? '';
  $('profileFamilyAndRelations').value = profile?.familyAndRelations ?? '';
  $('profileWork').value = profile?.work ?? '';
  $('profileLocation').value = profile?.location ?? '';
  $('profileCommunicationStyle').value = profile?.communicationStyle ?? '';
  $('profileInterests').value = profile?.interests ?? '';
  $('profileGoals').value = profile?.goals ?? '';
  $('profileConstraints').value = profile?.constraints ?? '';
  $('profileDetails').value = profile?.details ?? '';
  $('profileFacts').value = profile?.facts ?? '';
  renderAssistantIdentity();
  renderProfileMode();

  void memoryProfile;
}

function hasFoundationProfile(profile) {
  return Boolean(profile && [
    profile.username,
    profile.email,
    profile.fullName,
    profile.assistantName,
    profile.assistantDescription,
    profile.familyAndRelations,
    profile.work,
    profile.location,
    profile.communicationStyle,
    profile.interests,
    profile.goals,
    profile.constraints,
    profile.details,
    profile.facts
  ].some(value => String(value ?? '').trim()));
}

function profileSteps() {
  return [...document.querySelectorAll('[data-profile-step]')];
}

function renderProfileMode() {
  const steps = profileSteps();
  const isOnboarding = state.profileMode === 'onboarding';
  const activeStep = Math.max(0, Math.min(state.profileStep, steps.length - 1));
  state.profileStep = activeStep;
  $('foundationProfileForm').classList.toggle('onboarding-mode', isOnboarding);
  $('profileWizardSteps').hidden = !isOnboarding;
  $('profilePreviousStep').hidden = !isOnboarding || activeStep === 0;
  $('profileNextStep').hidden = !isOnboarding || activeStep === steps.length - 1;
  $('saveFoundationProfile').hidden = isOnboarding && activeStep !== steps.length - 1;
  $('saveFoundationProfile').textContent = isOnboarding && activeStep === steps.length - 1 ? 'Finish setup' : 'Save profile';
  $('cancelProfileModal').textContent = isOnboarding ? 'Skip for now' : 'Cancel';
  $('profileModalTitle').textContent = isOnboarding ? 'Set Up Your Local Assistant' : 'My Profile';

  if (isOnboarding) {
    const step = steps[activeStep];
    $('profileModalSubtitle').textContent = step?.dataset.profileStepDescription ?? 'Profile data is injected into every turn.';
    $('profileWizardSteps').innerHTML = steps.map((item, index) => `
      <span class="${index === activeStep ? 'active' : index < activeStep ? 'complete' : ''}">${escapeHtml(item.dataset.profileStepTitle ?? `Step ${index + 1}`)}</span>
    `).join('');
  } else {
    $('profileModalSubtitle').textContent = 'Explicit settings injected before learned memory.';
    $('profileWizardSteps').innerHTML = '';
  }

  steps.forEach((step, index) => {
    step.hidden = isOnboarding && index !== activeStep;
  });
}

async function saveFoundationProfile(event) {
  event.preventDefault();
  if (state.profileMode === 'onboarding' && state.profileStep < profileSteps().length - 1) {
    nextProfileStep();
    return;
  }

  await guarded(async () => {
    const result = await api('/user-profile/', {
      method: 'PUT',
      body: JSON.stringify({
        username: $('profileUsername').value,
        email: $('profileEmail').value,
        fullName: $('profileFullName').value,
        assistantName: $('profileAssistantName').value,
        assistantDescription: $('profileAssistantDescription').value,
        familyAndRelations: $('profileFamilyAndRelations').value,
        work: $('profileWork').value,
        location: $('profileLocation').value,
        communicationStyle: $('profileCommunicationStyle').value,
        interests: $('profileInterests').value,
        goals: $('profileGoals').value,
        constraints: $('profileConstraints').value,
        details: $('profileDetails').value,
        facts: $('profileFacts').value
      })
    });
    const saved = result.profile ?? result;
    state.foundationProfile = saved;
    renderFoundationProfile(saved, state.memoryProfile);
    renderAssistantIdentity();
    if (result.initialSetup) {
      setStatus(`Initial setup seeded ${result.initialSetup.memoriesWritten} memories, including ${result.initialSetup.relationshipsWritten} relationship edges.`, 'ok');
      await loadMemoryStats();
    }
    closeProfileModal();
  }, state.profileMode === 'onboarding' ? 'Saving profile and seeding memory...' : 'Saving profile...', { overlay: state.profileMode === 'onboarding' });
}

function openProfileModal(mode = 'edit') {
  state.profileMode = mode;
  state.profileStep = 0;
  renderFoundationProfile(state.foundationProfile, state.memoryProfile);
  $('profileModal').hidden = false;
  if (mode === 'onboarding') {
    ($('profileAssistantName').value ? $('profileAssistantDescription') : $('profileAssistantName')).focus();
  } else {
    $('profileFullName').focus();
  }
}

function closeProfileModal() {
  $('profileModal').hidden = true;
  state.profileMode = 'edit';
  state.profileStep = 0;
  renderProfileMode();
}

function nextProfileStep() {
  state.profileStep = Math.min(state.profileStep + 1, profileSteps().length - 1);
  renderProfileMode();
}

function previousProfileStep() {
  state.profileStep = Math.max(state.profileStep - 1, 0);
  renderProfileMode();
}

function renderMemoryDashboard(stats, profile) {
  const reflected = profile?.reflectedAt ? new Date(profile.reflectedAt).toLocaleString() : 'not reflected yet';
  renderMemoryDashboardStage(stats, profile, reflected);
}

function renderMemoryDashboardStage(stats, profile, reflected) {
  if (!$('memoryDashboardMain')) return;

  const facts = profile?.facts ?? [];
  const memoryCount = stats?.collections?.find(collection => collection.name === 'memory')?.recordCount ?? 0;
  const knowledgeCount = stats?.collections?.find(collection => collection.name === 'knowledge')?.recordCount ?? 0;
  $('memoryDashboardMain').innerHTML = `
    <section class="dashboard-hero">
      <button data-graph-scope="total" class="dashboard-scope ${graphScopeActive('total') ? 'active' : ''}" type="button"><span>Total Records</span><strong>${escapeHtml(stats?.recordCount ?? 0)}</strong></button>
      <button data-graph-scope="memory" class="dashboard-scope ${graphScopeActive('memory') ? 'active' : ''}" type="button"><span>Memory Vectors</span><strong>${escapeHtml(memoryCount)}</strong></button>
      <button data-graph-scope="knowledge" class="dashboard-scope ${graphScopeActive('knowledge') ? 'active' : ''}" type="button"><span>Knowledge Vectors</span><strong>${escapeHtml(knowledgeCount)}</strong></button>
      <button data-graph-scope="canonical" class="dashboard-scope ${graphScopeActive('canonical') ? 'active' : ''}" type="button"><span>Canonical Facts</span><strong>${escapeHtml(profile?.factCount ?? 0)}</strong></button>
    </section>
    <section class="dashboard-graph-panel">
      <div class="graph-type-filters">
        ${memoryGraphTypes().map(type => `<button data-graph-type="${escapeHtml(type)}" class="${state.memoryGraphTypes.includes(type) ? 'active' : ''}" type="button">${escapeHtml(type)}</button>`).join('')}
        <span class="graph-filter-spacer"></span>
        <label class="graph-related-toggle"><input id="memoryGraphOnlyRelated" type="checkbox" ${state.memoryGraphOnlyRelated ? 'checked' : ''}> display only selected</label>
        <input id="memoryGraphQuery" class="graph-search-input ${state.memoryGraphSearchOpen ? 'open' : ''}" value="${escapeHtml(state.memoryGraphQuery)}" placeholder="Search graph..." ${state.memoryGraphSearchOpen ? '' : 'hidden'}>
        <button id="openMemoryGraphSearch" class="graph-icon-button ${state.memoryGraphSearchOpen ? 'active' : ''}" type="button" aria-label="Search graph">${searchIconSvg()}</button>
        <button id="openMemoryReview" class="graph-icon-button ${state.memoryReviewOpen ? 'active' : ''}" type="button" aria-label="Review inbox">${messageIconSvg()}</button>
      </div>
      <div id="memoryGraphCanvas" class="memory-graph-canvas"></div>
      <div id="memoryGraphInspector" class="memory-graph-inspector"></div>
      <div id="memoryReviewInbox" class="memory-review-inbox" ${state.memoryReviewOpen ? '' : 'hidden'}></div>
    </section>
    <section class="dashboard-grid">
      <details class="canonical-profile-details">
        <summary>Recent Canonical Profile Layer <span>${escapeHtml(reflected)}</span></summary>
        ${facts.slice(0, 6).map(fact => `<p><span>${escapeHtml(fact.category || 'profile')}</span><b>${escapeHtml(fact.text)}</b></p>`).join('') || '<p class="muted">Run reflection to populate profile facts.</p>'}
      </details>
    </section>
  `;
  bindMemoryGraphControls();
  renderMemoryGraphCanvas();
}

function openMemoryDashboard() {
  closeOperationsStage();
  setLeftPanelCollapsed(true);
  $('memoryDashboardStage').hidden = false;
  renderMemoryDashboard(state.memoryStats, state.memoryProfile);
  loadMemoryGraph().catch(error => setStatus(error.message, 'error'));
}

function closeMemoryDashboard() {
  $('memoryDashboardStage').hidden = true;
  resetMemoryGraphDrilldown(false);
}

async function loadMemoryGraph(options = {}) {
  if (options.renderOnlyIfVisible && $('memoryDashboardStage')?.hidden) return;

  const query = state.memoryGraphQuery.trim();
  state.memoryGraph = await api('/memory/graph', {
    method: 'POST',
    body: JSON.stringify({
      layer: state.memoryGraphLayer,
      query: query || null,
      limit: query ? 72 : 96,
      focusId: state.memoryGraphFocusId,
      focusLabel: state.memoryGraphFocusLabel,
      types: state.memoryGraphTypes,
      filter: state.memoryGraphFilter
    })
  });
  state.memoryReview = await api('/memory/review');
  renderMemoryGraphCanvas();
  renderMemoryReviewInbox();
}

function bindMemoryGraphControls() {
  document.querySelectorAll('[data-graph-scope]').forEach(button => {
    button.addEventListener('click', () => {
      setMemoryGraphScope(button.dataset.graphScope);
      renderMemoryDashboard(state.memoryStats, state.memoryProfile);
      loadMemoryGraph().catch(error => setStatus(error.message, 'error'));
    });
  });
  $('openMemoryGraphSearch')?.addEventListener('click', () => {
    if (state.memoryGraphSearchOpen) {
      const value = $('memoryGraphQuery')?.value?.trim() ?? '';
      if (value !== state.memoryGraphQuery) {
        submitMemoryGraphSearch(value);
        return;
      }
    }

    state.memoryGraphSearchOpen = !state.memoryGraphSearchOpen;
    renderMemoryDashboard(state.memoryStats, state.memoryProfile);
    if (state.memoryGraphSearchOpen) {
      setTimeout(() => $('memoryGraphQuery')?.focus(), 0);
    }
  });
  $('openMemoryReview')?.addEventListener('click', () => {
    state.memoryReviewOpen = !state.memoryReviewOpen;
    renderMemoryDashboard(state.memoryStats, state.memoryProfile);
    renderMemoryReviewInbox();
  });
  $('memoryGraphOnlyRelated')?.addEventListener('change', event => {
    state.memoryGraphOnlyRelated = event.target.checked;
    renderMemoryGraphCanvas();
  });
  $('memoryGraphQuery')?.addEventListener('keydown', event => {
    if (event.key === 'Enter') {
      submitMemoryGraphSearch(event.target.value);
    }
  });
  document.querySelectorAll('[data-graph-type]').forEach(button => {
    button.addEventListener('click', () => {
      const type = button.dataset.graphType;
      state.memoryGraphTypes = state.memoryGraphTypes.includes(type)
        ? state.memoryGraphTypes.filter(existing => existing !== type)
        : [...state.memoryGraphTypes, type];
      renderMemoryDashboard(state.memoryStats, state.memoryProfile);
      loadMemoryGraph().catch(error => setStatus(error.message, 'error'));
    });
  });
}

function searchIconSvg() {
  return '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6.5"></circle><path d="m16 16 4 4"></path></svg>';
}

function messageIconSvg() {
  return '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 5h14v10H8l-3 3V5Z"></path><path d="M8 9h8M8 12h5"></path></svg>';
}

function graphScopeActive(scope) {
  return state.memoryGraphScope === scope;
}

function setMemoryGraphScope(scope) {
  state.memoryGraphScope = scope ?? 'total';
  state.memoryGraphFocusId = null;
  state.memoryGraphFocusLabel = null;
  state.memoryGraphDrillLevel = 0;
  state.memoryGraphQuery = '';
  state.memoryGraphSearchOpen = false;
  state.memoryGraphTypes = [];
  state.memoryGraphFilter = null;
  resetMemoryGraphView();

  if (scope === 'memory') {
    state.memoryGraphLayer = 'memory';
  } else if (scope === 'knowledge') {
    state.memoryGraphLayer = 'knowledge';
  } else if (scope === 'canonical') {
    state.memoryGraphLayer = 'memory';
    state.memoryGraphFilter = { kind: 'canonical_profile_fact' };
  } else {
    state.memoryGraphLayer = 'all';
    state.memoryGraphScope = 'total';
  }
}

function submitMemoryGraphSearch(value) {
  state.memoryGraphQuery = value.trim();
  state.memoryGraphSearchOpen = false;
  state.memoryGraphFocusId = null;
  state.memoryGraphFocusLabel = null;
  state.memoryGraphDrillLevel = 0;
  renderMemoryDashboard(state.memoryStats, state.memoryProfile);
  loadMemoryGraph().catch(error => setStatus(error.message, 'error'));
}

function setMemoryGraphZoom(value) {
  state.memoryGraphZoom = Math.max(0.62, Math.min(2.6, value));
  renderMemoryGraphCanvas();
}

function resetMemoryGraphDrilldown(reload = true) {
  state.memoryGraphFocusId = null;
  state.memoryGraphFocusLabel = null;
  state.memoryGraphDrillLevel = 0;
  resetMemoryGraphView();
  if (reload) {
    return loadMemoryGraph().catch(error => setStatus(error.message, 'error'));
  }

  renderMemoryGraphCanvas();
  return Promise.resolve();
}

function resetMemoryGraphView() {
  state.memoryGraphZoom = 1;
  state.memoryGraphPanX = 0;
  state.memoryGraphPanY = 0;
}

function renderMemoryGraphCanvas() {
  const canvas = $('memoryGraphCanvas');
  if (!canvas) return;

  const graph = state.memoryGraph;
  if (!graph) {
    canvas.innerHTML = '<div class="graph-empty"><span></span><strong>Awaiting vector field</strong><p>Open the dashboard or run a query to project memory and knowledge clusters.</p></div>';
    renderMemoryGraphInspector(null);
    return;
  }

  const zoom = state.memoryGraphZoom;
  const panX = state.memoryGraphPanX;
  const panY = state.memoryGraphPanY;
  const center = 300;
  const project = node => ({
    x: center + ((node.x - 50) * 5.15 * zoom),
    y: center + ((node.y - 50) * 5.15 * zoom)
  });
  const edges = graph.edges ?? [];
  const nodes = graph.nodes ?? [];
  const isDrilled = Boolean(state.memoryGraphFocusId);
  const showRecords = state.memoryGraphDrillLevel >= 2;
  const graphView = selectVisibleGraph(nodes, edges, showRecords);
  const visibleNodes = graphView.nodes;
  const visibleEdges = graphView.edges;
  const visibleNodeById = new Map(visibleNodes.map(node => [node.id, node]));

  canvas.innerHTML = `
    <svg class="memory-graph-svg" viewBox="0 0 600 600" role="img" aria-label="${escapeHtml(graph.layer)} vector graph" tabindex="0">
      <defs>
        <radialGradient id="graphGlow" cx="50%" cy="50%" r="50%"><stop offset="0%" stop-color="#38bdf8" stop-opacity="0.8"/><stop offset="100%" stop-color="#14b8a6" stop-opacity="0.02"/></radialGradient>
        <filter id="nodeGlow"><feGaussianBlur stdDeviation="3.5" result="blur"/><feMerge><feMergeNode in="blur"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
      </defs>
      <circle cx="300" cy="300" r="238" class="graph-orbit"></circle>
      <circle cx="300" cy="300" r="145" class="graph-orbit inner"></circle>
      <g class="graph-viewport" transform="translate(${panX.toFixed(1)} ${panY.toFixed(1)})">
        ${visibleEdges.map(edge => renderGraphEdge(edge, visibleNodeById, project, isDrilled)).join('')}
        ${visibleNodes.map(node => renderGraphNode(node, project, showRecords)).join('')}
      </g>
    </svg>
    <div class="graph-map-controls" aria-label="Graph map controls">
      <button id="memoryGraphZoomOut" type="button" aria-label="Zoom out">-</button>
      <button id="memoryGraphZoomIn" type="button" aria-label="Zoom in">+</button>
      <button id="memoryGraphReset" type="button">Reset</button>
    </div>
    <div class="graph-readout"><strong>${escapeHtml(graph.layer)}</strong><span>${escapeHtml(graph.recordCount)} records · ${escapeHtml(visibleNodes.filter(node => node.kind === 'concept').length)} ${isDrilled ? (showRecords ? 'record source view' : 'branch nodes') : 'top categories'} · zoom ${Math.round(zoom * 100)}%</span>${isDrilled ? '<button id="memoryGraphBack" type="button">Back To Overview</button>' : ''}</div>
  `;
  canvas.querySelectorAll('[data-graph-node]').forEach(element => {
    element.addEventListener('click', event => {
      event.preventDefault();
      event.stopPropagation();
      if (element.dataset.graphKind === 'record' && state.memoryGraphFocusId) {
        openGraphMemoryRecord(element.dataset.graphCollection || state.memoryGraphLayer, element.dataset.graphNode);
        return;
      }

      focusMemoryGraphNode(element.dataset.graphNode);
    });
    element.addEventListener('keydown', event => {
      if (event.key === 'Enter' || event.key === ' ') {
        event.preventDefault();
        focusMemoryGraphNode(element.dataset.graphNode);
      }
    });
  });
  bindMemoryGraphViewport(canvas);
  $('memoryGraphZoomIn')?.addEventListener('click', () => setMemoryGraphZoom(state.memoryGraphZoom + 0.18));
  $('memoryGraphZoomOut')?.addEventListener('click', () => setMemoryGraphZoom(state.memoryGraphZoom - 0.18));
  $('memoryGraphReset')?.addEventListener('click', () => {
    setMemoryGraphScope('total');
    renderMemoryDashboard(state.memoryStats, state.memoryProfile);
    loadMemoryGraph().catch(error => setStatus(error.message, 'error'));
  });
  $('memoryGraphBack')?.addEventListener('click', () => {
    resetMemoryGraphDrilldown();
  });
  canvas.querySelector('.memory-graph-svg')?.addEventListener('dblclick', event => {
    if (!event.target.closest?.('[data-graph-node]')) {
      resetMemoryGraphDrilldown();
    }
  });
  renderMemoryGraphInspector(nodes.find(node => node.recordId === state.memoryGraphFocusId || node.id === state.memoryGraphFocusId) ?? null);
}

function selectVisibleGraph(nodes, edges, showRecords) {
  const baseNodes = showRecords ? nodes : nodes.filter(node => node.kind !== 'record');
  const baseNodeIds = new Set(baseNodes.map(node => node.id));
  const baseEdges = (showRecords ? edges : edges.filter(edge => edge.kind !== 'mentions'))
    .filter(edge => baseNodeIds.has(edge.source) && baseNodeIds.has(edge.target));

  if (!state.memoryGraphOnlyRelated || !state.memoryGraphFocusId) {
    return { nodes: baseNodes, edges: baseEdges };
  }

  const selected = nodes.find(node => node.id === state.memoryGraphFocusId || node.recordId === state.memoryGraphFocusId);

  if (!selected) {
    return { nodes: baseNodes, edges: baseEdges };
  }

  const selectedIds = new Set([selected.id]);

  for (const edge of edges.filter(edge => edge.kind === 'mentions' && (edge.source === selected.id || edge.target === selected.id))) {
    selectedIds.add(edge.source);
    selectedIds.add(edge.target);
  }

  const filteredNodes = nodes.filter(node => selectedIds.has(node.id));
  const filteredNodeIds = new Set(filteredNodes.map(node => node.id));
  const filteredEdges = edges.filter(edge => edge.kind === 'mentions' && filteredNodeIds.has(edge.source) && filteredNodeIds.has(edge.target));

  return { nodes: filteredNodes, edges: filteredEdges };
}

function renderGraphEdge(edge, nodeById, project, isDrilled = false) {
  const source = nodeById.get(edge.source);
  const target = nodeById.get(edge.target);
  if (!source || !target) return '';
  const start = project(source);
  const end = project(target);
  const hasRelation = Boolean(source.metadata?.relation || source.metadata?.relatedTo || target.metadata?.relation || target.metadata?.relatedTo);
  return `<line class="graph-edge ${escapeHtml(edge.kind)} ${hasRelation ? 'relation' : ''}" x1="${start.x.toFixed(1)}" y1="${start.y.toFixed(1)}" x2="${end.x.toFixed(1)}" y2="${end.y.toFixed(1)}" stroke-width="${Math.max(0.45, edge.weight * (edge.kind === 'mentions' ? 3.0 : 2.1)).toFixed(2)}"></line>`;
}

function renderGraphNode(node, project, isDrilled = false) {
  const point = project(node);
  const isConcept = node.kind === 'concept';
  const isDrilledRecord = isDrilled && node.kind === 'record';
  const radius = isConcept ? Math.min(42, 13 + node.weight * 2.6) : isDrilledRecord ? 7.5 : 5.5;
  const selected = node.recordId === state.memoryGraphFocusId || node.id === state.memoryGraphFocusId;
  const hitRadius = Math.max(radius + 8, isConcept ? 24 : isDrilledRecord ? 18 : 16);
  const nodeKey = node.recordId ?? node.id;
  const title = graphNodeTitle(node);

  if (isDrilledRecord) {
    const type = node.memoryType ?? 'record';
    return `
      <g class="graph-node related-record ${escapeHtml(type)} ${selected ? 'selected' : ''}" data-graph-node="${escapeHtml(nodeKey)}" data-graph-kind="record" data-graph-collection="${escapeHtml(node.metadata?.layer ?? state.memoryGraphLayer)}" tabindex="0" role="button" aria-label="Open ${escapeHtml(node.label)}" transform="translate(${point.x.toFixed(1)} ${point.y.toFixed(1)})">
        <title>${escapeHtml(title)}</title>
        <circle class="graph-node-hit" r="${hitRadius.toFixed(1)}"></circle>
        <circle r="${radius.toFixed(1)}" filter="url(#nodeGlow)"></circle>
      </g>
    `;
  }

  return `
    <g class="graph-node ${escapeHtml(node.kind)} ${escapeHtml(node.memoryType ?? '')} ${selected ? 'selected' : ''}" data-graph-node="${escapeHtml(nodeKey)}" data-graph-kind="${escapeHtml(node.kind)}" data-graph-collection="${escapeHtml(node.metadata?.layer ?? state.memoryGraphLayer)}" tabindex="0" role="button" aria-label="Inspect ${escapeHtml(node.label)}" transform="translate(${point.x.toFixed(1)} ${point.y.toFixed(1)})">
      <title>${escapeHtml(title)}</title>
      <circle class="graph-node-hit" r="${hitRadius.toFixed(1)}"></circle>
      <circle r="${radius.toFixed(1)}" filter="url(#nodeGlow)"></circle>
      ${isConcept ? `<text y="${(radius + 13).toFixed(1)}">${escapeHtml(truncateMiddle(node.label, 22))}</text>` : ''}
    </g>
  `;
}

function graphNodeTitle(node) {
  const pieces = [
    node.label,
    node.memoryType,
    node.provenance,
    node.metadata?.memoryState,
    node.observedAt ? new Date(node.observedAt).toLocaleString() : null,
    node.textPreview
  ].filter(Boolean);
  return pieces.join('\n');
}

async function focusMemoryGraphNode(id) {
  const node = state.memoryGraph?.nodes?.find(item => item.id === id || item.recordId === id);
  const nextFocusId = node?.recordId ?? node?.id ?? id;

  if (state.memoryGraphFocusId && nextFocusId === state.memoryGraphFocusId) {
    if (node?.kind === 'concept' && state.memoryGraphDrillLevel === 1) {
      state.memoryGraphDrillLevel = 2;
      renderMemoryGraphCanvas();
      await inspectMemoryGraphNode(node);
      return;
    }

    return;
  }

  const previousWasFocused = Boolean(state.memoryGraphFocusId);

  state.memoryGraphFocusId = nextFocusId;
  state.memoryGraphFocusLabel = node?.label ?? null;
  state.memoryGraphDrillLevel = node?.kind === 'concept' ? (previousWasFocused ? 2 : 1) : 2;
  renderMemoryGraphCanvas();

  if (!node) {
    renderMemoryGraphInspector(null);
    return;
  }

  if (node.kind === 'concept') {
    await loadMemoryGraph();
  }

  await inspectMemoryGraphNode(node);
}

async function inspectMemoryGraphNode(node) {
  const inspector = $('memoryGraphInspector');
  if (inspector) {
    inspector.innerHTML = `<strong>${escapeHtml(node.label)}</strong><p class="muted">Loading related Qdrant records...</p>`;
  }

  try {
    const query = state.memoryGraphQuery.trim();
    const result = await api('/memory/graph/inspect', {
      method: 'POST',
      body: JSON.stringify({
        layer: state.memoryGraphLayer,
        query: query || null,
        limit: 24,
        nodeId: node.id,
        recordId: node.recordId ?? null,
        label: node.label,
        types: state.memoryGraphTypes,
        filter: state.memoryGraphFilter
      })
    });
    renderMemoryGraphInspector(node, result);
  } catch (error) {
    setStatus(error.message, 'error');
    renderMemoryGraphInspector(node);
  }
}

function bindMemoryGraphViewport(canvas) {
  const svg = canvas.querySelector('.memory-graph-svg');
  const viewport = canvas.querySelector('.graph-viewport');
  if (!svg) return;

  svg.addEventListener('wheel', event => {
    event.preventDefault();
    setMemoryGraphZoom(state.memoryGraphZoom + (event.deltaY < 0 ? 0.12 : -0.12));
  }, { passive: false });

  let dragging = false;
  let startX = 0;
  let startY = 0;
  let panX = 0;
  let panY = 0;

  svg.addEventListener('pointerdown', event => {
    if (event.target.closest?.('[data-graph-node]')) return;
    dragging = true;
    startX = event.clientX;
    startY = event.clientY;
    panX = state.memoryGraphPanX;
    panY = state.memoryGraphPanY;
    svg.setPointerCapture(event.pointerId);
    svg.classList.add('panning');
  });

  svg.addEventListener('pointermove', event => {
    if (!dragging) return;
    state.memoryGraphPanX = Math.max(-220, Math.min(220, panX + event.clientX - startX));
    state.memoryGraphPanY = Math.max(-220, Math.min(220, panY + event.clientY - startY));
    if (viewport) {
      viewport.setAttribute('transform', `translate(${state.memoryGraphPanX.toFixed(1)} ${state.memoryGraphPanY.toFixed(1)})`);
    }
  });

  svg.addEventListener('pointerup', event => {
    dragging = false;
    svg.releasePointerCapture?.(event.pointerId);
    svg.classList.remove('panning');
  });
}

function renderMemoryGraphInspector(node, related = null) {
  const inspector = $('memoryGraphInspector');
  if (!inspector) return;

  if (!node) {
    inspector.innerHTML = '<strong>Memory Tree</strong><p>Top-level nodes are stable categories, tags, topics, and projects. Click one to zoom into its record vectors, then inspect individual sources.</p>';
    return;
  }

  const metadata = Object.entries(node.metadata ?? {}).slice(0, 8).map(([key, value]) => `<span>${escapeHtml(key)}=${escapeHtml(value)}</span>`).join('');
  const relatedRecords = dedupeGraphRelatedRecords(related?.records ?? []);
  inspector.innerHTML = `
    <strong>${escapeHtml(node.label)}</strong>
    <div class="graph-badges">${node.memoryType ? `<span>${escapeHtml(node.memoryType)}</span>` : ''}${node.provenance ? `<span>${escapeHtml(node.provenance)}</span>` : ''}${node.metadata?.memoryState ? `<span>${escapeHtml(node.metadata.memoryState)}</span>` : ''}${node.observedAt ? `<span>${escapeHtml(new Date(node.observedAt).toLocaleString())}</span>` : ''}</div>
    <p>${escapeHtml(node.textPreview ?? `${node.weight} linked vector${node.weight === 1 ? '' : 's'}`)}</p>
    <div class="metadata-row">${metadata || `<span>${escapeHtml(node.kind)}</span>`}</div>
    ${node.recordId ? `<button data-open-memory-record="${escapeHtml(node.recordId)}" data-open-memory-layer="${escapeHtml(node.metadata?.layer ?? state.memoryGraphLayer)}" type="button">Open Source Record</button>` : ''}
    ${related ? `<div class="graph-related-records"><strong>Related Qdrant Records</strong>${relatedRecords.map(renderGraphRelatedRecord).join('') || '<p class="muted">No related records found.</p>'}</div>` : ''}
  `;
  inspector.querySelector('[data-open-memory-record]')?.addEventListener('click', event => openGraphMemoryRecord(event.target.dataset.openMemoryLayer, event.target.dataset.openMemoryRecord));
  inspector.querySelectorAll('[data-open-related-record]').forEach(button => {
    button.addEventListener('click', () => openGraphMemoryRecord(button.dataset.relatedCollection, button.dataset.openRelatedRecord));
  });
}

function renderGraphRelatedRecord(record) {
  const concepts = (record.matchedConcepts ?? []).slice(0, 4).map(concept => `<span>${escapeHtml(concept)}</span>`).join('');
  return `
    <article class="graph-related-record">
      <div class="graph-badges"><span>${escapeHtml(record.memoryType ?? 'record')}</span><span>${escapeHtml(record.provenance ?? 'unknown')}</span><span>${escapeHtml(record.memoryState ?? 'active')}</span></div>
      <p>${escapeHtml(record.text)}</p>
      <div class="metadata-row">${concepts || '<span>related</span>'}</div>
      <button data-open-related-record="${escapeHtml(record.id)}" data-related-collection="${escapeHtml(record.collection)}" type="button">Inspect Record</button>
    </article>
  `;
}

function dedupeGraphRelatedRecords(records) {
  const seen = new Set();
  return records.filter(record => {
    const key = record.id || `${record.collection}|${record.text}|${record.memoryType ?? ''}|${record.provenance ?? ''}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function memoryGraphTypes() {
  return ['identity', 'relationship', 'preference', 'opinion', 'interest', 'goal', 'project', 'constraint', 'fact', 'schema', 'knowledge'];
}

async function openGraphMemoryRecord(collection, id) {
  await guarded(async () => {
    const record = await api(`/memory/collections/${encodeURIComponent(collection)}/records/${encodeURIComponent(id)}`);
    const why = await api(`/memory/collections/${encodeURIComponent(collection)}/records/${encodeURIComponent(id)}/why`);
    $('memoryGraphInspector').innerHTML = `
      <strong>Source Record</strong>
      <div class="graph-badges"><span>${escapeHtml(collection)}</span><span>${escapeHtml(record.metadata?.memoryType ?? 'record')}</span><span>${escapeHtml(record.metadata?.provenance ?? 'unknown provenance')}</span><span>${escapeHtml(why.memoryState ?? 'active')}</span></div>
      <p>${escapeHtml(record.text)}</p>
      <div class="source-explanation"><strong>Why this is known</strong>${(why.explanation ?? []).map(line => `<span>${escapeHtml(line)}</span>`).join('')}</div>
      <div class="metadata-row">${Object.entries(record.metadata ?? {}).map(([key, value]) => `<span>${escapeHtml(key)}=${escapeHtml(value)}</span>`).join('')}</div>
      <div class="memory-record-actions">
        ${record.metadata?.sourceConversationId ? `<button id="openSourceSession" type="button">Open Source Session</button>` : ''}
        <button data-memory-action="edit" data-memory-collection="${escapeHtml(collection)}" data-memory-id="${escapeHtml(id)}" type="button">Edit as New</button>
        <button data-memory-action="supersede" data-memory-collection="${escapeHtml(collection)}" data-memory-id="${escapeHtml(id)}" type="button">Supersede</button>
        <button data-memory-action="forget" data-memory-collection="${escapeHtml(collection)}" data-memory-id="${escapeHtml(id)}" type="button">Forget</button>
      </div>
    `;
    $('openSourceSession')?.addEventListener('click', () => openSession(record.metadata.sourceConversationId));
    bindMemoryTransitionActions($('memoryGraphInspector'));
  }, 'Opening memory source...', { overlay: false });
}

function renderMemoryReviewInbox() {
  const container = $('memoryReviewInbox');
  if (!container) return;

  const review = state.memoryReview;
  if (!review) {
    container.innerHTML = '<strong>Review Inbox</strong><p class="muted">Loading memory review signals...</p>';
    return;
  }

  const duplicateGroups = review.duplicateGroups ?? [];
  const pending = review.pending ?? [];
  container.innerHTML = `
    <strong>Review Inbox</strong>
    <p>${escapeHtml(review.pendingCount)} pending · ${escapeHtml(review.duplicateGroupCount)} duplicate groups</p>
    ${pending.slice(0, 3).map(renderReviewItem).join('')}
    ${duplicateGroups.slice(0, 4).map(group => `<details><summary>${escapeHtml(group.memoryType ?? 'duplicate')} · ${escapeHtml(group.records.length)} records</summary>${group.records.slice(0, 4).map(renderReviewItem).join('')}</details>`).join('')}
  `;
  bindMemoryTransitionActions(container);
}

function renderReviewItem(item) {
  return `<article class="review-item"><span>${escapeHtml(item.memoryType ?? 'record')} · ${escapeHtml(item.provenance ?? 'unknown')} · ${escapeHtml(item.confidence ?? '-')} · ${escapeHtml(item.memoryState ?? 'active')}</span><p>${escapeHtml(item.textPreview)}</p><div class="memory-record-actions"><button data-memory-action="open" data-memory-collection="${escapeHtml(item.collection)}" data-memory-id="${escapeHtml(item.id)}" type="button">Why</button><button data-memory-action="edit" data-memory-collection="${escapeHtml(item.collection)}" data-memory-id="${escapeHtml(item.id)}" type="button">Edit</button><button data-memory-action="supersede" data-memory-collection="${escapeHtml(item.collection)}" data-memory-id="${escapeHtml(item.id)}" type="button">Supersede</button><button data-memory-action="forget" data-memory-collection="${escapeHtml(item.collection)}" data-memory-id="${escapeHtml(item.id)}" type="button">Forget</button></div></article>`;
}

function bindMemoryTransitionActions(root = document) {
  root.querySelectorAll('[data-memory-action]').forEach(button => {
    button.addEventListener('click', () => runMemoryTransition(button.dataset.memoryCollection, button.dataset.memoryId, button.dataset.memoryAction));
  });
}

async function runMemoryTransition(collection, id, action) {
  if (!collection || !id || !action) return;

  if (action === 'open') {
    await openGraphMemoryRecord(collection, id);
    return;
  }

  await guarded(async () => {
    if (action === 'forget') {
      const why = prompt('Why should this memory be forgotten?', 'Incorrect or no longer useful');
      if (why === null) return;
      await api(`/memory/collections/${encodeURIComponent(collection)}/records/${encodeURIComponent(id)}/forget`, {
        method: 'POST',
        body: JSON.stringify({ why })
      });
    } else {
      const current = await api(`/memory/collections/${encodeURIComponent(collection)}/records/${encodeURIComponent(id)}`);
      const text = prompt(action === 'edit' ? 'Replacement memory text:' : 'Superseding memory text:', current.text ?? '');
      if (text === null || !text.trim()) return;
      const why = prompt('Why is this change correct?', action === 'edit' ? 'Corrected by review' : 'Newer information supersedes it');
      if (why === null) return;
      await api(`/memory/collections/${encodeURIComponent(collection)}/records/${encodeURIComponent(id)}/${action}`, {
        method: 'POST',
        body: JSON.stringify({ text, why })
      });
    }

    await Promise.all([loadMemoryGraph({ renderOnlyIfVisible: true }), loadMemoryStats()]);
    await openGraphMemoryRecord(collection, id);
  }, 'Updating memory state...', { overlay: false });
}

const operationPanels = {
  runtime: ['Runtime', 'Core process, model, and session health.'],
  jobs: ['Background Jobs', 'Queued and running local work.'],
  documents: ['Documents', 'Upload, OCR, and invoice extraction.'],
  mcp: ['MCP Bridge', 'Local MCP tool registration and approvals.'],
  api: ['API Discovery', 'Local API introspection helpers.'],
  memory: ['Memory Actions', 'Manual memory writes and reflection.'],
  details: ['Conversation Details', 'Trace, metrics, citations, and reasoning for the selected response.'],
  help: ['Help', 'Short guide to the control surfaces.']
};

function openOperationsStage(panel) {
  closeMemoryDashboard();
  const [title, subtitle] = operationPanels[panel] ?? operationPanels.runtime;
  $('operationsEyebrow').textContent = 'Operations';
  $('operationsTitle').textContent = title;
  $('operationsSubtitle').textContent = subtitle;
  document.querySelectorAll('.operations-panel').forEach(element => {
    element.hidden = element.id !== `operations${capitalize(panel)}Panel`;
  });
  $('operationsStage').hidden = false;
}

function closeOperationsStage() {
  $('operationsStage').hidden = true;
}

function capitalize(value) {
  return value ? value[0].toUpperCase() + value.slice(1) : value;
}

async function loadMemoryCollections() {
  state.memoryCollections = await api('/memory/collections');
  const selectedExists = state.memoryCollections.some(collection => collection.name === state.selectedCollection);
  const coordinator = state.memoryCollections.find(collection => collection.name === 'coordinator');
  const selected = selectedExists ? state.selectedCollection : coordinator?.name ?? state.memoryCollections[0]?.name ?? 'coordinator';
  state.selectedCollection = selected;
  $('memoryCollectionSelect').innerHTML = state.memoryCollections.map(collection => `
    <option value="${escapeHtml(collection.name)}" ${collection.name === selected ? 'selected' : ''}>${escapeHtml(humanizeCollectionName(collection.name))} (${escapeHtml(collection.recordCount)} memories)</option>
  `).join('') || '<option value="coordinator">coordinator</option>';
}

function humanizeCollectionName(name) {
  return name
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, character => character.toUpperCase());
}

function bindMemoryRecordActions(root = document) {
  root.querySelectorAll('[data-view-memory]').forEach(button => {
    button.addEventListener('click', () => viewMemoryRecord(button.closest('[data-memory-record]')));
  });
  root.querySelectorAll('[data-edit-memory]').forEach(button => {
    button.addEventListener('click', () => editMemoryRecord(button.closest('[data-memory-record]')));
  });
  root.querySelectorAll('[data-delete-memory]').forEach(button => {
    button.addEventListener('click', () => deleteMemoryRecord(button.closest('[data-memory-record]')));
  });
}

function renderMetadata(metadata) {
  return Object.entries(metadata ?? {})
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => `<span>${escapeHtml(key)}=${escapeHtml(value)}</span>`)
    .join('');
}

function parseMetadataFilter(input) {
  return input
    .split(/\s+/)
    .map(part => part.trim())
    .filter(Boolean)
    .reduce((filter, part) => {
      const separator = part.indexOf('=');

      if (separator > 0 && separator < part.length - 1) {
        filter[part.slice(0, separator)] = part.slice(separator + 1);
      }

      return filter;
    }, {});
}

function formatMetadataFilter(filter) {
  return Object.entries(filter ?? {}).map(([key, value]) => `${key}=${value}`).join(' ');
}

async function deleteCollection(name) {
  if (!confirm(`Delete memory collection ${name}? This removes its vectors from the active store.`)) return;
  await guarded(async () => {
    await api(`/memory/collections/${encodeURIComponent(name)}`, { method: 'DELETE' });
    await Promise.all([loadMemoryStats(), loadMemoryCollections()]);
  }, 'Deleting collection...');
}

async function viewMemoryRecord(element) {
  if (!element) return;
  await guarded(async () => {
    const record = await api(memoryRecordPath(element));
    renderMemoryRecordDetail(element, record);
  }, 'Loading memory...', { overlay: false });
}

async function editMemoryRecord(element) {
  if (!element) return;
  await guarded(async () => {
    const record = await api(memoryRecordPath(element));
    renderMemoryRecordEditor(element, record);
  }, 'Opening memory editor...', { overlay: false });
}

async function saveMemoryRecord(element) {
  await guarded(async () => {
    const text = element.querySelector('[data-memory-text]').value.trim();
    if (!text) throw new Error('Memory text is required.');
    const metadata = parseMetadataFilter(element.querySelector('[data-memory-metadata]').value);
    const record = await api(memoryRecordPath(element), {
      method: 'PUT',
      body: JSON.stringify({ text, metadata })
    });
    renderMemoryRecordDetail(element, record);
    await loadMemoryStats();
  }, 'Saving memory...', { overlay: false });
}

async function deleteMemoryRecord(element) {
  if (!element || !confirm('Delete this memory record?')) return;
  await guarded(async () => {
    await api(memoryRecordPath(element), { method: 'DELETE' });
    element.remove();
    await loadMemoryStats();
  }, 'Deleting memory...', { overlay: false });
}

function renderMemoryRecordDetail(element, record) {
  element.querySelector('p').textContent = record.text || '(empty text)';
  element.querySelector('.record-head span').textContent = `${record.textLength} chars`;
  element.querySelector('.metadata-row').innerHTML = renderMetadata(record.metadata ?? {}) || '<span>no metadata</span>';
  element.querySelector('.memory-record-actions').innerHTML = `
    <button data-memory-action="open" data-memory-collection="${escapeHtml(record.collection)}" data-memory-id="${escapeHtml(record.id)}" type="button">Why</button>
    <button data-edit-memory="${escapeHtml(record.id)}" type="button">Edit</button>
    <button data-memory-action="supersede" data-memory-collection="${escapeHtml(record.collection)}" data-memory-id="${escapeHtml(record.id)}" type="button">Supersede</button>
    <button data-memory-action="forget" data-memory-collection="${escapeHtml(record.collection)}" data-memory-id="${escapeHtml(record.id)}" type="button">Forget</button>
    <button data-delete-memory="${escapeHtml(record.id)}" type="button">Delete</button>
  `;
  bindMemoryRecordActions(element);
  bindMemoryTransitionActions(element);
}

function renderMemoryRecordEditor(element, record) {
  element.querySelector('p').innerHTML = `
    <textarea data-memory-text rows="5">${escapeHtml(record.text)}</textarea>
    <input data-memory-metadata value="${escapeHtml(formatMetadataFilter(record.metadata ?? {}))}" placeholder="tenant=max category=preference">
  `;
  element.querySelector('.record-head span').textContent = `${record.textLength} chars`;
  element.querySelector('.metadata-row').innerHTML = renderMetadata(record.metadata ?? {}) || '<span>no metadata</span>';
  element.querySelector('.memory-record-actions').innerHTML = `
    <button data-save-memory="${escapeHtml(record.id)}" type="button">Save</button>
    <button data-view-memory="${escapeHtml(record.id)}" type="button">Cancel</button>
    <button data-delete-memory="${escapeHtml(record.id)}" type="button">Delete</button>
  `;
  element.querySelector('[data-save-memory]').addEventListener('click', () => saveMemoryRecord(element));
  bindMemoryRecordActions(element);
}

function memoryRecordPath(element) {
  return `/memory/collections/${encodeURIComponent(element.dataset.memoryCollection)}/records/${encodeURIComponent(element.dataset.memoryRecord)}`;
}

async function loadConsolidationJobs() {
  state.consolidationJobs = await api('/memory/consolidation/jobs');
  const visibleJobs = state.consolidationJobs.filter(job => job.sessionId === state.sessionId).slice(0, 3);
  $('consolidationJobs').innerHTML = visibleJobs.map(job => `
    <div class="job ${escapeHtml(job.status)}"><strong>${escapeHtml(job.status)}</strong><span>${escapeHtml(job.sessionId.slice(0, 8))} · ${escapeHtml(String(job.memoriesWritten))} memories</span></div>
  `).join('') || '<p class="muted">No consolidation jobs for this session.</p>';
}

async function loadBackgroundJobs() {
  state.backgroundJobs = await api('/background-jobs');
  await refreshCurrentSessionIfJobsCompleted(state.backgroundJobs);
  const recent = state.backgroundJobs
    .filter(job => job.sessionId === state.sessionId || (!job.sessionId && ['memory_reflection'].includes(job.kind)))
    .slice(0, 8);
  $('backgroundJobs').innerHTML = recent.map(renderBackgroundJobCard).join('') || '<p class="muted">No background jobs for this session.</p>';

  document.querySelectorAll('[data-cancel-job]').forEach(button => {
    button.addEventListener('click', () => cancelBackgroundJob(button.dataset.cancelJob));
  });

  recent.filter(job => job.status === 'completed').forEach(job => loadJobArtifacts(job.id).catch(() => {}));
}

function renderBackgroundJobCard(job) {
  const total = job.progressTotal || 0;
  const current = job.progressCurrent || 0;
  const percent = total > 0 ? Math.min(100, Math.round((current / total) * 100)) : 0;
  const canCancel = job.status === 'queued' || job.status === 'running';
  const phase = jobPhase(job);
  const summary = jobSummary(job);
  const detailsOpen = job.status === 'running' || job.status === 'failed' ? ' open' : '';

  return `
    <details class="background-job ${escapeHtml(job.status)}"${detailsOpen}>
      <summary>
        <span class="job-main"><strong>${escapeHtml(job.title ?? job.kind)}</strong><small>${escapeHtml(job.kind)} · ${escapeHtml(shortId(job.id))}</small></span>
        <span class="status-pill ${escapeHtml(job.status)}">${escapeHtml(job.status)}</span>
      </summary>
      <div class="job-progress-row">
        <progress value="${escapeHtml(String(current))}" max="${escapeHtml(String(Math.max(total, current, 1)))}"></progress>
        <span>${escapeHtml(total > 0 ? `${percent}%` : phase)}</span>
      </div>
      <p class="job-summary">${escapeHtml(summary)}</p>
      ${job.error ? `<em>${escapeHtml(job.error)}</em>` : ''}
      <div class="job-meta">
        <span>${escapeHtml(phase)}</span>
        <span>${escapeHtml(total > 0 ? `${current}/${total}` : 'no progress total')}</span>
        <span>${escapeHtml(formatDateTime(job.updatedAt))}</span>
      </div>
      <div class="job-artifacts" data-job-artifacts="${escapeHtml(job.id)}"></div>
      ${canCancel ? `<button data-cancel-job="${escapeHtml(job.id)}" type="button">Cancel job</button>` : ''}
    </details>
  `;
}

function jobPhase(job) {
  if (job.status === 'queued') return 'queued';
  if (job.status === 'completed') return 'complete';
  if (job.status === 'failed') return 'failed';
  if (job.status === 'cancelled') return 'cancelled';
  const message = String(job.statusMessage ?? '').toLowerCase();
  if (message.includes('synthes')) return 'synthesizing';
  if (message.includes('brows')) return 'browsing';
  if (message.includes('vector')) return 'vectorizing';
  if (message.includes('ocr')) return 'reading';
  return 'running';
}

function jobSummary(job) {
  return truncateMiddle(job.statusMessage || job.result || job.error || 'Waiting for progress...', 260);
}

function shortId(value) {
  return String(value ?? '').slice(0, 8);
}

function formatDateTime(value) {
  if (!value) return 'unknown time';
  try {
    return new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(new Date(value));
  } catch {
    return String(value);
  }
}

async function refreshCurrentSessionIfJobsCompleted(jobs) {
  const completed = jobs.some(job => {
    const previous = knownJobStates.get(job.id);
    knownJobStates.set(job.id, job.status);
    return previous && previous !== job.status && job.status === 'completed' && job.sessionId === state.sessionId;
  });

  if (completed && state.sessionId) {
    const session = await api(`/sessions/${state.sessionId}`);
    renderMessages(session.messages ?? []);
    await loadSessions();
  }
}

async function loadJobArtifacts(jobId) {
  const container = document.querySelector(`[data-job-artifacts="${CSS.escape(jobId)}"]`);

  if (!container) return;

  const artifacts = await api(`/background-jobs/${jobId}/artifacts`);
  container.innerHTML = artifacts.length
    ? `<span>Artifacts</span>${artifacts.map(artifact => `
      <button data-view-artifact="${escapeHtml(jobId)}:${escapeHtml(artifact.id)}" type="button">${escapeHtml(artifact.title)}</button>
    `).join('')}`
    : '';
  container.querySelectorAll('[data-view-artifact]').forEach(button => {
    button.addEventListener('click', () => viewArtifact(button.dataset.viewArtifact));
  });
}

async function viewArtifact(value) {
  const [jobId, artifactId] = value.split(':');
  const response = await fetch(`/background-jobs/${encodeURIComponent(jobId)}/artifacts/${encodeURIComponent(artifactId)}`);

  if (!response.ok) throw new Error(await response.text());

  const content = await response.text();
  $('artifactViewer').hidden = false;
  $('artifactViewer').innerHTML = `
    <div class="artifact-head"><span><strong>Artifact Preview</strong><small>${escapeHtml(`${content.length} chars`)}</small></span><button id="closeArtifact" type="button">Close</button></div>
    <div class="markdown-body">${renderMarkdown(content)}</div>
  `;
  $('closeArtifact').addEventListener('click', () => { $('artifactViewer').hidden = true; });
}

async function loadTaskGraph() {
  renderTaskGraph(null, 'Select an assistant message to inspect its task graph.');
}

function renderTaskGraph(graph, emptyText = 'No task graph for this message.') {
  if (!graph) {
    $('taskGraph').innerHTML = `<p class="muted">${escapeHtml(emptyText)}</p>`;
    return;
  }

  const artifacts = graph.artifacts ?? [];
  const events = graph.events ?? [];
  $('taskGraph').innerHTML = `
    <div class="graph-head"><strong>${escapeHtml(graph.status)}</strong><span>${escapeHtml((graph.nodes ?? []).length)} nodes · ${escapeHtml(Math.round((graph.confidence ?? 0) * 100))}%</span></div>
    <p>${escapeHtml(graph.goal)}</p>
    <div class="node-list">
      ${(graph.nodes ?? []).map(node => `
        <div class="node mini ${escapeHtml(node.status)}">
          <span>${escapeHtml(node.kind ?? 'turn')} · ${escapeHtml(node.status)}</span>
          <strong>${escapeHtml(node.title)}</strong>
          ${node.blocker ? `<em>${escapeHtml(node.blocker)}</em>` : ''}
        </div>
      `).join('')}
    </div>
    <div class="artifact-list">
      ${artifacts.slice(-4).map(artifact => `
        <details class="graph-detail">
          <summary>${escapeHtml(artifact.kind)}: ${escapeHtml(artifact.title)}</summary>
          <pre>${escapeHtml(artifact.content)}</pre>
        </details>
      `).join('')}
    </div>
    <div class="event-list">
      ${events.slice(-6).map(item => `
        <details class="graph-detail" ${item.kind.startsWith('tool_') ? 'open' : ''}>
          <summary>${escapeHtml(item.kind)}</summary>
          <pre>${escapeHtml(item.content)}</pre>
        </details>
      `).join('')}
    </div>
  `;
}

async function newSession() {
  const now = new Date().toISOString();
  state.draftSession = {
    id: `draft-${crypto.randomUUID()}`,
    title: 'LLLMax session',
    agent: 'coordinator',
    createdAt: now,
    updatedAt: now,
    messages: []
  };
  state.sessionId = null;
  knownJobStates = new Map();
  $('sessionTitle').textContent = state.draftSession.title;
  renderMessages([]);
  clearResponseDetails();
  $('memoryResults').innerHTML = '';
  $('artifactViewer').hidden = true;
  await Promise.all([loadSessions(), loadMemoryStats()]);
  await Promise.all([loadTaskGraph(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadPatchProposals(), loadMcpServers()]);
  renderRuntime(await api('/health'));
}

async function deleteAllSessions() {
  await guarded(async () => {
    await api('/sessions', { method: 'DELETE' });
    state.sessionId = null;
    $('sessionTitle').textContent = 'Ready';
    renderMessages([]);
    clearResponseDetails();
    await Promise.all([loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs()]);
    await newSession();
  }, 'Deleting sessions...');
}

async function resetEverything() {
  const confirmation = prompt('This deletes all local sessions, profile rows, jobs, documents, integrations, and vector memory. Type RESET EVERYTHING to continue.');
  if (confirmation !== 'RESET EVERYTHING') return;

  await guarded(async () => {
    const result = await api('/memory/reset-everything', {
      method: 'POST',
      body: JSON.stringify({ confirm: confirmation })
    });
    state.sessionId = null;
    state.draftSession = null;
    state.foundationProfile = null;
    state.memoryProfile = null;
    knownJobStates = new Map();
    $('sessionTitle').textContent = 'Ready';
    $('memoryResults').textContent = `Reset complete. Deleted ${result.deletedCollectionCount} vector collections and ${result.deletedSqliteRows} SQLite rows.`;
    $('artifactViewer').hidden = true;
    renderMessages([]);
    clearResponseDetails();
    await Promise.all([loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadPatchProposals(), loadMcpServers(), loadTools(), loadSkills()]);
    await newSession();
    $('memoryResults').textContent = `Reset complete. Deleted ${result.deletedCollectionCount} vector collections and ${result.deletedSqliteRows} SQLite rows.`;
    openProfileModal('onboarding');
  }, 'Resetting local data...');
}

async function openSession(id) {
  const session = await api(`/sessions/${id}`);
  state.draftSession = null;
  state.sessionId = session.id;
  knownJobStates = new Map();
  $('sessionTitle').textContent = session.title;
  $('modelSelect').value = state.coordinatorModel?.coordinatorOverride ?? '';
  state.selectedTraceId = null;
  renderMessages(session.messages ?? []);
  await Promise.all([loadSessions(), loadTaskGraph(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadPatchProposals(), loadMcpServers()]);
  renderRuntime(await api('/health'));
}

async function ensurePersistedSession() {
  if (state.sessionId) return;
  const session = await api('/sessions', {
    method: 'POST',
    body: JSON.stringify({
      title: state.draftSession?.title ?? 'LLLMax session',
      agent: state.draftSession?.agent ?? 'coordinator'
    })
  });
  state.sessionId = session.id;
  state.draftSession = null;
}

async function deleteSession(id) {
  if (!confirm('Delete this session?')) return;
  await guarded(async () => {
    await api(`/sessions/${encodeURIComponent(id)}`, { method: 'DELETE' });

    if (state.sessionId === id) {
      await newSession();
      return;
    }

    await loadSessions();
  }, 'Deleting session...');
}

async function sendMessage(event) {
  event.preventDefault();
  if (state.isStreaming) {
    setInlineProgress('Still finishing the previous response...');
    return;
  }

  const message = $('prompt').value.trim();
  if (!message) return;

  state.isStreaming = true;
  state.activeAssistantId = `draft-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  await guarded(async () => {
    await ensurePersistedSession();
    $('prompt').value = '';
    $('sendButton').disabled = true;
    $('prompt').disabled = true;
    const existingMessages = documentMessages();
    renderMessages([...existingMessages, { role: 'user', content: message }, { role: 'assistant', content: '', draftId: state.activeAssistantId }]);
    resetInspectPanel();
    setInlineProgress('Starting coordinator...');

    const result = await streamChat(`/sessions/${state.sessionId}/chat/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        message,
        reasoningEffort: effortMap[$('effort').value],
        allowTools: $('allowTools').checked,
        persistToMemory: $('persistMemory').checked
      })
    });

    applyFinalResponse(result);
    renderMetrics(result.metrics);
    const assistantTrace = [...state.messageTraces.entries()].at(-1);

    if (assistantTrace) {
      selectMessageTrace(assistantTrace[0]);
    }
    await Promise.all([loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadPatchProposals(), loadMcpServers(), loadTools(), loadSkills()]);
  }, 'Thinking...', { overlay: false }).finally(() => {
    clearInlineProgress();
    $('sendButton').disabled = false;
    $('prompt').disabled = false;
    state.isStreaming = false;
    state.activeAssistantId = null;
  });
}

async function streamChat(path, options) {
  const response = await fetch(path, options);

  if (!response.ok || !response.body) {
    throw new Error(await response.text());
  }

  const decoder = new TextDecoder();
  const reader = response.body.getReader();
  let buffer = '';
  let finalResult = null;
  let progressHistory = [];

  let streamError = null;

  try {
    while (true) {
      const { value, done } = await reader.read();

      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const parts = buffer.split('\n\n');
      buffer = parts.pop() ?? '';

      for (const part of parts) {
        const event = parseServerEvent(part);
        if (!event) continue;

        if (event.type === 'chunk' && event.content) {
          clearInlineProgress();
          appendAssistantChunk(event.content);
        }

        if (event.type === 'progress' && event.content) {
          appendInspectEvent(event);
          if (event.payload?.runtimeEvent?.kind !== 'model_delta') {
            progressHistory = [...progressHistory, formatProgressEvent(event)].slice(-6);
            setInlineProgress(progressHistory);
          }
          if (event.payload?.graph) {
            state.taskGraph = event.payload.graph;
          }
        }

        if (event.type === 'task_graph' && event.payload) {
          state.taskGraph = event.payload;
        }

        if (event.type === 'final') {
          clearInlineProgress();
          finalResult = event.result;
          if (finalResult) {
            applyFinalResponse(finalResult);
          }
        }

        if (event.type === 'error') {
          throw new Error(event.content ?? 'Streaming chat failed.');
        }
      }
    }

    if (buffer.trim()) {
      const event = parseServerEvent(buffer);
      if (event?.type === 'final') {
        finalResult = event.result;
        if (finalResult) {
          applyFinalResponse(finalResult);
        }
      }
    }
  } catch (error) {
    streamError = error;
  }

  if (!finalResult) {
    try {
      finalResult = await recoverFinalResponse();
    } catch (recoveryError) {
      throw streamError ?? recoveryError;
    }
  }

  return finalResult;
}

function formatProgressEvent(event) {
  const runtimeEvent = event.payload?.runtimeEvent;
  const tool = runtimeEvent?.tool;
  const kind = runtimeEvent?.kind ?? 'progress';
  const agentPrefix = tool?.includes('/') ? tool.split('/').slice(0, -1).map(part => `[${part}]`).join(' ') + ' ' : '';
  const label = progressLabel(kind, tool);
  return {
    kind,
    label,
    content: `${agentPrefix}${event.content}`,
    status: progressStatus(kind)
  };
}

function progressLabel(kind, tool) {
  if (tool) return friendlyToolName(tool.split('/').at(-1));
  if (kind === 'model_delta') return 'researcher draft';
  if (kind === 'model_started') return 'model';
  return kind.replaceAll('_', ' ');
}

function progressStatus(kind) {
  if (kind === 'tool_completed') return 'complete';
  if (kind === 'tool_failed') return 'failed';
  if (kind === 'model_delta') return 'running';
  if (kind === 'tool_started' || kind === 'tool_progress') return 'running';
  return 'active';
}

async function recoverFinalResponse() {
  if (!state.sessionId) {
    throw new Error('Streaming chat ended without a final response.');
  }

  setInlineProgress('Recovering saved response...');
  const session = await api(`/sessions/${encodeURIComponent(state.sessionId)}`);
  const messages = session.messages ?? [];
  const last = messages[messages.length - 1];

  if (last?.role !== 'assistant' || !last.content) {
    throw new Error('Streaming chat ended before a final response was saved.');
  }

  return {
    sessionId: state.sessionId,
    response: last.content,
    messages,
    metrics: null,
    reasoningSteps: last.reasoningSteps ?? [],
    summarized: false,
    route: null
  };
}

function applyFinalResponse(result) {
  if (!result?.messages) return;
  state.activeAssistantId = null;
  renderMessages(result.messages);
}

function parseServerEvent(raw) {
  const dataLine = raw.split('\n').find(line => line.startsWith('data: '));
  if (!dataLine) return null;

  try {
    return JSON.parse(dataLine.slice(6));
  } catch {
    return null;
  }
}

function appendAssistantChunk(content) {
  const messages = $('messages');
  const assistantMessages = [...messages.querySelectorAll('.message.assistant')];
  const target = activeAssistantElement(assistantMessages) ?? assistantMessages[assistantMessages.length - 1];

  if (!target) return;

  target.dataset.raw = (target.dataset.raw ?? '') + content;
  target.querySelector('.markdown-body').innerHTML = renderMarkdown(target.dataset.raw);
  const messageIndex = [...messages.querySelectorAll('.message')].indexOf(target);
  if (messageIndex >= 0 && state.currentMessages[messageIndex]) {
    state.currentMessages[messageIndex] = { ...state.currentMessages[messageIndex], content: target.dataset.raw };
  }
  messages.scrollTop = messages.scrollHeight;
}

function activeAssistantElement(assistantMessages) {
  return state.activeAssistantId
    ? assistantMessages.find(element => element.dataset.draftId === state.activeAssistantId)
    : null;
}

function setInlineProgress(content) {
  const messages = $('messages');
  let progress = $('inlineProgress');
  const items = (Array.isArray(content) ? content : [content || 'Thinking...']).map(normalizeProgressItem);

  if (!progress) {
    progress = document.createElement('div');
    progress.id = 'inlineProgress';
    progress.className = 'inline-progress';
    messages.appendChild(progress);
  }

  progress.innerHTML = `<span class="inline-spinner"></span><div class="inline-progress-stack">${items.map((item, index) => `
    <span class="inline-progress-step ${escapeHtml(item.status)} ${index === items.length - 1 ? 'current' : ''}">
      <strong>${escapeHtml(item.label)}</strong><span>${escapeHtml(item.content)}</span>
    </span>
  `).join('')}</div>`;
  messages.scrollTop = messages.scrollHeight;
}

function normalizeProgressItem(item) {
  if (typeof item === 'object' && item !== null) {
    return {
      label: item.label ?? 'progress',
      content: item.content ?? '',
      status: item.status ?? 'active'
    };
  }

  return { label: 'progress', content: String(item ?? 'Thinking...'), status: 'active' };
}

function clearInlineProgress() {
  $('inlineProgress')?.remove();
}

function documentMessages() {
  return state.currentMessages.length
    ? state.currentMessages
    : [...document.querySelectorAll('.message')].map(element => ({
      role: element.classList.contains('user') ? 'user' : 'assistant',
      content: element.dataset.raw ?? element.textContent
    }));
}

function renderMarkdown(markdown) {
  const codeBlocks = [];
  const escaped = escapeHtml(markdown ?? '').replace(/```([\s\S]*?)```/g, (_, code) => {
    const token = `@@CODE_BLOCK_${codeBlocks.length}@@`;
    codeBlocks.push(`<pre><code>${code.replace(/^\n|\n$/g, '')}</code></pre>`);
    return token;
  });
  const lines = escaped.split('\n');
  const html = [];
  let paragraph = [];
  let list = null;

  const closeParagraph = () => {
    if (paragraph.length) {
      html.push(`<p>${paragraph.join('<br>')}</p>`);
      paragraph = [];
    }
  };
  const closeList = () => {
    if (list) {
      html.push(`</${list}>`);
      list = null;
    }
  };

  for (const line of lines) {
    if (line.startsWith('@@CODE_BLOCK_')) {
      closeParagraph();
      closeList();
      html.push(codeBlocks[Number(line.match(/@@CODE_BLOCK_(\d+)@@/)?.[1] ?? 0)] ?? '');
      continue;
    }

    if (!line.trim()) {
      closeParagraph();
      closeList();
      continue;
    }

    const heading = line.match(/^(#{1,3})\s+(.+)$/);
    if (heading) {
      closeParagraph();
      closeList();
      html.push(`<h${heading[1].length}>${renderInlineMarkdown(heading[2])}</h${heading[1].length}>`);
      continue;
    }

    const unordered = line.match(/^\s*[-*]\s+(.+)$/);
    if (unordered) {
      closeParagraph();
      if (list !== 'ul') {
        closeList();
        list = 'ul';
        html.push('<ul>');
      }
      html.push(`<li>${renderInlineMarkdown(unordered[1])}</li>`);
      continue;
    }

    const ordered = line.match(/^\s*\d+\.\s+(.+)$/);
    if (ordered) {
      closeParagraph();
      if (list !== 'ol') {
        closeList();
        list = 'ol';
        html.push('<ol>');
      }
      html.push(`<li>${renderInlineMarkdown(ordered[1])}</li>`);
      continue;
    }

    closeList();
    paragraph.push(renderInlineMarkdown(line));
  }

  closeParagraph();
  closeList();
  return html.join('');
}

function renderInlineMarkdown(value) {
  const code = [];
  let html = value.replace(/`([^`]+)`/g, (_, content) => {
    const token = `@@CODE_${code.length}@@`;
    code.push(`<code>${content}</code>`);
    return token;
  });

  html = html.replace(/\[([^\]]+)\]\(([^\s)]+)\)/g, (_, text, url) => {
    const normalized = url.replaceAll('&amp;', '&');
    if (!/^(https?:\/\/|\/)/i.test(normalized)) {
      return text;
    }

    return `<a href="${escapeHtml(normalized)}" target="_blank" rel="noreferrer">${text}</a>`;
  });
  html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  html = html.replace(/\*([^*]+)\*/g, '<em>$1</em>');

  return html.replace(/@@CODE_(\d+)@@/g, (_, index) => code[Number(index)] ?? '');
}

async function uploadDocument() {
  await guarded(async () => {
    const file = $('documentFile').files[0];
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    const result = await api('/documents/upload', { method: 'POST', body: form });
    state.uploadedDocumentId = result.id;
    $('documentStatus').textContent = `Uploaded ${result.fileName} as ${result.id}`;
  }, 'Uploading document...');
}

async function runDocumentAction(path) {
  await guarded(async () => {
    if (!state.uploadedDocumentId) throw new Error('Upload a document first.');
    const result = await api(path, {
      method: 'POST',
      body: JSON.stringify({ documentId: state.uploadedDocumentId, model: $('modelSelect').value || null })
    });
    $('documentStatus').textContent = path.includes('invoice') ? result.json : result.text;
    await loadMemoryStats();
  }, 'Running document model...');
}

async function discoverApi() {
  await guarded(async () => {
    const result = await api('/integrations/apis/discover', {
      method: 'POST',
      body: JSON.stringify({ name: $('apiName').value, baseUrl: $('apiBaseUrl').value })
    });
    $('apiResult').textContent = JSON.stringify(result, null, 2);
  }, 'Discovering API...');
}

async function addMemory() {
  await guarded(async () => {
    const collection = $('memoryCollectionSelect').value || 'coordinator';
    const text = $('memoryText').value.trim();

    if (!text) throw new Error('Memory text is required.');

    await api('/memory/upsert', {
      method: 'POST',
      body: JSON.stringify({ collection, text, metadata: parseMetadataFilter($('memoryMetadata').value) })
    });
    $('memoryText').value = '';
    $('memoryMetadata').value = '';
    await loadMemoryStats();
    $('memoryResults').textContent = `Saved memory in ${collection}.`;
  }, 'Saving memory...');
}

function renderMemorySearchResults(collection, query, results) {
  $('memoryResults').innerHTML = `
    <div class="search-summary"><strong>${escapeHtml(collection)}</strong><span>${escapeHtml(results.length)} semantic matches for ${escapeHtml(query)}</span></div>
    <div class="collection-records">
      ${results.map(result => {
        const metadata = renderMetadata(result.metadata ?? {});
        return `
          <article class="collection-record" data-memory-record="${escapeHtml(result.id)}" data-memory-collection="${escapeHtml(collection)}">
            <div class="record-head"><code>${escapeHtml(result.id.slice(0, 12))}</code><span>score ${escapeHtml(Number(result.score).toFixed(3))}</span></div>
            <p>${escapeHtml(result.text || '(empty text)')}</p>
            <div class="metadata-row">${metadata || '<span>no metadata</span>'}</div>
            <div class="memory-record-actions">
              <button data-view-memory="${escapeHtml(result.id)}" type="button">View</button>
              <button data-edit-memory="${escapeHtml(result.id)}" type="button">Edit</button>
              <button data-memory-action="open" data-memory-collection="${escapeHtml(collection)}" data-memory-id="${escapeHtml(result.id)}" type="button">Why</button>
              <button data-memory-action="supersede" data-memory-collection="${escapeHtml(collection)}" data-memory-id="${escapeHtml(result.id)}" type="button">Supersede</button>
              <button data-memory-action="forget" data-memory-collection="${escapeHtml(collection)}" data-memory-id="${escapeHtml(result.id)}" type="button">Forget</button>
              <button data-delete-memory="${escapeHtml(result.id)}" type="button">Delete</button>
            </div>
          </article>
        `;
      }).join('') || '<p class="muted">No semantic matches.</p>'}
    </div>
  `;
  bindMemoryRecordActions($('memoryResults'));
  bindMemoryTransitionActions($('memoryResults'));
}

async function consolidateSession() {
  await guarded(async () => {
    if (!state.sessionId) throw new Error('Open a session first.');
    await api('/memory/consolidation', {
      method: 'POST',
      body: JSON.stringify({ sessionId: state.sessionId })
    });
    await Promise.all([loadMemoryStats(), loadConsolidationJobs()]);
  }, 'Syncing durable memory...');
}

async function reflectMemory() {
  await guarded(async () => {
    const job = await api('/background-jobs/', {
      method: 'POST',
      body: JSON.stringify({
        kind: 'memory_reflection',
        title: 'Reflect canonical profile memory',
        payload: { limit: 100 },
        notifySession: false
      })
    });
    openOperationsStage('jobs');
    await loadBackgroundJobs();
    await loadMemoryStats();
    setStatus(`Memory reflection queued (${shortId(job.id)}). Completion will stay visible in Background Jobs.`, 'ok');
  }, 'Scheduling memory reflection...', { overlay: false });
}

async function cancelBackgroundJob(id) {
  await guarded(async () => {
    await api(`/background-jobs/${id}/cancel`, { method: 'POST' });
    await loadBackgroundJobs();
  }, 'Cancelling background job...');
}

async function decideApproval(id, approve, scope = 'once') {
  await guarded(async () => {
    await api(`/approvals/${id}/${approve ? 'approve' : 'reject'}`, {
      method: 'POST',
      body: JSON.stringify({ reason: approve ? `Approved in local UI (${scope}).` : 'Rejected in local UI.', scope })
    });
    await Promise.all([loadApprovals(), loadPatchProposals(), loadTools()]);
  }, approve ? 'Approving request...' : 'Rejecting request...');
}

async function registerMcp() {
  await guarded(async () => {
    const name = $('mcpName').value.trim();
    const endpoint = $('mcpEndpoint').value.trim();
    const tool = $('mcpTool').value.trim();
    if (!name || !endpoint || !tool) throw new Error('MCP name, endpoint, and tool are required.');
    await api('/mcp/servers', {
      method: 'POST',
      body: JSON.stringify({
        name,
        transport: 'http',
        endpoint,
        tools: [{ name: tool, description: `MCP tool ${tool}`, argumentsJsonSchema: { type: 'object', additionalProperties: true } }]
      })
    });
    await Promise.all([loadMcpServers(), loadTools()]);
  }, 'Registering MCP tool...');
}

async function decideMcpTool(value, approve) {
  await guarded(async () => {
    const [serverId, toolName] = value.split(':');
    await api(`/mcp/servers/${serverId}/tools/${encodeURIComponent(toolName)}/${approve ? 'approve' : 'reject'}`, { method: 'POST' });
    await Promise.all([loadMcpServers(), loadTools()]);
  }, approve ? 'Approving MCP tool...' : 'Rejecting MCP tool...');
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

$('newSession').addEventListener('click', () => guarded(newSession, 'Creating session...'));
$('deleteSessions').addEventListener('click', deleteAllSessions);
$('chatForm').addEventListener('submit', sendMessage);
$('prompt').addEventListener('keydown', event => {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    $('chatForm').requestSubmit();
  }
});
$('effort').addEventListener('input', event => $('effortLabel').textContent = effortMap[event.target.value]);
$('modelSelect').addEventListener('change', event => guarded(() => setCoordinatorModel(event.target.value), 'Updating coordinator model...', { overlay: false }));
$('uploadDocument').addEventListener('click', uploadDocument);
$('runOcr').addEventListener('click', () => runDocumentAction('/documents/ocr'));
$('extractInvoice').addEventListener('click', () => runDocumentAction('/documents/extract-invoice'));
$('discoverApi').addEventListener('click', discoverApi);
$('foundationProfileForm').addEventListener('submit', saveFoundationProfile);
$('openProfileModal').addEventListener('click', () => openProfileModal('edit'));
$('closeProfileModal').addEventListener('click', closeProfileModal);
$('cancelProfileModal').addEventListener('click', closeProfileModal);
$('profileNextStep').addEventListener('click', nextProfileStep);
$('profilePreviousStep').addEventListener('click', previousProfileStep);
$('profileModal').addEventListener('click', event => {
  if (event.target.id === 'profileModal') closeProfileModal();
});
$('openMemoryDashboard').addEventListener('click', openMemoryDashboard);
$('closeMemoryDashboard').addEventListener('click', closeMemoryDashboard);
$('toggleLeftPanel').addEventListener('click', () => setLeftPanelCollapsed(!state.leftPanelCollapsed));
document.querySelectorAll('[data-open-operations]').forEach(button => {
  button.addEventListener('click', () => openOperationsStage(button.dataset.openOperations));
});
$('closeOperationsStage').addEventListener('click', closeOperationsStage);
$('addMemory').addEventListener('click', addMemory);
$('consolidateSession').addEventListener('click', consolidateSession);
$('reflectMemory').addEventListener('click', reflectMemory);
$('resetEverything').addEventListener('click', resetEverything);
$('registerMcp').addEventListener('click', registerMcp);
$('saveAgentPrompt').addEventListener('click', saveAgentPrompt);
$('resetAgentPrompt').addEventListener('click', resetAgentPrompt);
$('cancelAgentPrompt').addEventListener('click', closeAgentPromptModal);
$('closeAgentPrompt').addEventListener('click', closeAgentPromptModal);
$('agentPromptModal').addEventListener('click', event => {
  if (event.target.id === 'agentPromptModal') closeAgentPromptModal();
});

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
}

clearResponseDetails();
setLeftPanelCollapsed(true);

await guarded(async () => {
  await Promise.all([loadModels(), loadAgents(), loadTools(), loadSkills(), loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadPatchProposals(), loadMcpServers()]);
  renderRuntime(await api('/health'));
  const shouldOnboard = !hasFoundationProfile(state.foundationProfile);
  if (state.sessions.length > 0) {
    await openSession(state.sessions[0].id);
  } else {
    await newSession();
  }
  if (shouldOnboard) {
    openProfileModal('onboarding');
  }
}, 'Bootstrapping local runtime...');

setInterval(() => {
  if (document.visibilityState === 'visible') {
    loadBackgroundJobs().catch(() => {});
  }
}, 3000);
