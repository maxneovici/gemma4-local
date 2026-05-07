const state = {
  sessionId: null,
  uploadedDocumentId: null,
  models: [],
  sessions: [],
  tools: [],
  agents: [],
  taskGraph: null,
  backgroundJobs: [],
  memoryCollections: [],
  selectedCollection: null,
  collectionInspectCursor: null,
  collectionInspectFilter: {},
  selectedTenant: null,
  selectedCategory: null,
  draftSession: null,
  visibleSessionCount: 12,
  messageTraces: new Map(),
  selectedTraceId: null,
  consolidationJobs: [],
  approvals: [],
  mcpServers: []
};

let knownJobStates = new Map();

const $ = id => document.getElementById(id);
const effortMap = ['low', 'auto', 'high'];

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
  $('workLabel').textContent = label;
}

function renderMessages(messages) {
  clearInlineProgress();
  const previousTraceId = state.selectedTraceId;
  state.messageTraces = new Map();
  $('messages').innerHTML = messages.filter(message => message.role !== 'progress').map((message, index) => renderMessage(message, index)).join('');
  document.querySelectorAll('[data-trace-id]').forEach(element => {
    element.addEventListener('click', () => selectMessageTrace(element.dataset.traceId));
  });

  if (previousTraceId && state.messageTraces.has(previousTraceId)) {
    selectMessageTrace(previousTraceId);
  } else {
    state.selectedTraceId = null;
    renderTaskGraph(null, 'Select an assistant message to inspect its task graph.');
    renderSteps([]);
  }

  $('messages').scrollTop = $('messages').scrollHeight;
}

function renderMessage(message, index) {
  const role = message.role === 'user' ? 'user' : 'assistant';
  const raw = message.content ?? '';

  if (role === 'user') {
    return `<div class="message user" data-raw="${escapeHtml(raw)}">${escapeHtml(raw)}</div>`;
  }

  const hasTrace = Boolean(message.reasoningSteps?.length || message.taskGraph || message.toolTraces?.length || message.citations?.length);

  if (!hasTrace) {
    return `<div class="message assistant" data-raw="${escapeHtml(raw)}"><div class="markdown-body">${renderMarkdown(raw)}</div></div>`;
  }

  const traceId = message.traceId ?? `message-${index}`;
  state.messageTraces.set(traceId, {
    message,
    graph: message.taskGraph ?? null,
    reasoningSteps: message.reasoningSteps ?? [],
    toolTraces: message.toolTraces ?? [],
    citations: message.citations ?? []
  });

  return `<div class="message assistant ${hasTrace ? 'has-trace' : ''} ${traceId === state.selectedTraceId ? 'selected' : ''}" data-raw="${escapeHtml(raw)}" data-trace-id="${escapeHtml(traceId)}"><div class="markdown-body">${renderMarkdown(raw)}</div>${hasTrace ? '<span class="trace-hint">trace</span>' : ''}</div>`;
}

function renderRuntime(health) {
  const highModel = state.models.find(model => model.name === 'gemma4:31b');
  $('runtime').innerHTML = `
    <div class="runtime-pill ${health?.isHealthy ? 'healthy' : 'unknown'}"><span>ollama</span><strong>${health?.isHealthy ? 'healthy' : 'unknown'}</strong></div>
    <button id="runtimeModels" class="runtime-pill runtime-models" type="button"><span>models</span><strong>${escapeHtml(String(state.models.length))}</strong></button>
    <div class="runtime-pill"><span>31b</span><strong>${escapeHtml(highModel ? `${highModel.sizeGb} GB` : 'missing')}</strong></div>
    <div class="runtime-pill ${state.sessionId ? 'healthy' : ''}"><span>session</span><strong>${state.sessionId ? 'active' : 'none'}</strong></div>
    <div id="modelDropdown" class="model-dropdown" hidden>
      ${state.models.map(model => `<button type="button" data-select-model="${escapeHtml(model.name)}"><strong>${escapeHtml(model.name)}</strong><span>${escapeHtml(model.sizeGb)} GB</span></button>`).join('') || '<p class="muted">No local models found.</p>'}
    </div>
  `;
  $('runtimeModels').addEventListener('click', () => {
    $('modelDropdown').hidden = !$('modelDropdown').hidden;
  });
  document.querySelectorAll('[data-select-model]').forEach(button => {
    button.addEventListener('click', () => {
      $('modelSelect').value = button.dataset.selectModel;
      $('modelDropdown').hidden = true;
    });
  });
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

function renderToolTraces(toolTraces) {
  $('toolTraces').innerHTML = toolTraces?.length
    ? toolTraces.map(trace => `
      <details class="tool-trace ${escapeHtml(trace.status ?? '')}">
        <summary><strong>${escapeHtml(trace.tool)}</strong><span>${escapeHtml(trace.status ?? 'unknown')}${trace.durationMs ? ` · ${Math.round(trace.durationMs)}ms` : ''}</span></summary>
        ${trace.arguments ? `<pre>args: ${escapeHtml(JSON.stringify(trace.arguments, null, 2))}</pre>` : ''}
        ${trace.result ? `<pre>${escapeHtml(trace.result)}</pre>` : ''}
        ${trace.error ? `<pre>${escapeHtml(trace.error)}</pre>` : ''}
      </details>
    `).join('')
    : '<p class="muted">No tool calls recorded for this message.</p>';
}

function renderCitations(citations) {
  $('citations').innerHTML = citations?.length
    ? citations.map(citation => {
      const title = citation.url
        ? `<a href="${escapeHtml(citation.url)}" target="_blank" rel="noreferrer">${escapeHtml(citation.title)}</a>`
        : escapeHtml(citation.title);
      const detail = [citation.kind, citation.source, citation.chunk ? `chunk ${citation.chunk}` : null, citation.score ? `score ${Number(citation.score).toFixed(3)}` : null]
        .filter(Boolean)
        .join(' · ');
      return `<div class="citation"><strong>${title}</strong><span>${escapeHtml(detail)}</span></div>`;
    }).join('')
    : '<p class="muted">No citations recorded for this message.</p>';
}

async function loadModels() {
  state.models = await api('/models');
  $('modelSelect').innerHTML = '<option value="">Auto route</option>' + state.models.map(model => `
    <option value="${escapeHtml(model.name)}">${escapeHtml(model.name)}</option>
  `).join('');
}

async function loadAgents() {
  state.agents = await api('/agents');
  $('agents').innerHTML = state.agents.map(agent => `
    <button type="button" title="${escapeHtml(agent.description)}">${escapeHtml(agent.name)}<br><small>${agent.allowedTools.length} tools</small></button>
  `).join('');
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

async function loadApprovals() {
  state.approvals = await api('/approvals');
  const pending = state.approvals.filter(approval => approval.status === 'pending');
  const recent = [...pending, ...state.approvals.filter(approval => approval.status !== 'pending')].slice(0, 5);
  $('approvals').innerHTML = recent.map(approval => `
    <div class="approval ${escapeHtml(approval.status)}">
      <strong>${escapeHtml(approval.title)}</strong>
      <span>${escapeHtml(approval.status)} · ${escapeHtml(approval.kind)}${approval.scope ? ` · ${escapeHtml(approval.scope)}` : ''}</span>
      <p>${escapeHtml(approval.description)}</p>
      ${approval.status === 'pending' ? `<div class="button-row"><button data-approve-once="${escapeHtml(approval.id)}" type="button">Allow once</button><button data-approve-session="${escapeHtml(approval.id)}" type="button">Allow session</button><button data-approve-persist="${escapeHtml(approval.id)}" type="button">Persist</button><button data-reject="${escapeHtml(approval.id)}" type="button">Reject</button></div>` : ''}
    </div>
  `).join('') || '<p class="muted">No approval requests.</p>';

  document.querySelectorAll('[data-approve-once]').forEach(button => button.addEventListener('click', () => decideApproval(button.dataset.approveOnce, true, 'once')));
  document.querySelectorAll('[data-approve-session]').forEach(button => button.addEventListener('click', () => decideApproval(button.dataset.approveSession, true, 'session')));
  document.querySelectorAll('[data-approve-persist]').forEach(button => button.addEventListener('click', () => decideApproval(button.dataset.approvePersist, true, 'persistent')));
  document.querySelectorAll('[data-reject]').forEach(button => button.addEventListener('click', () => decideApproval(button.dataset.reject, false)));
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
  $('memoryStats').innerHTML = `
    <div class="runtime-pill"><span>provider</span><strong>${escapeHtml(stats.provider)}</strong></div>
    <div class="runtime-pill"><span>collections</span><strong>${stats.collectionCount}</strong></div>
    <div class="runtime-pill"><span>records</span><strong>${stats.recordCount}</strong></div>
  `;
  await loadMemoryCollections();
}

async function loadMemoryCollections() {
  state.memoryCollections = await api('/memory/collections');
  const selected = state.selectedCollection ?? state.memoryCollections[0]?.name ?? 'coordinator';
  $('memoryCollectionSelect').innerHTML = state.memoryCollections.map(collection => `
    <option value="${escapeHtml(collection.name)}" ${collection.name === selected ? 'selected' : ''}>${escapeHtml(collection.name)}</option>
  `).join('') || '<option value="coordinator">coordinator</option>';
  renderQdrantCollectionTable();
}

function renderQdrantCollectionTable() {
  if (!$('qdrantCollectionTable')) return;

  $('qdrantCollectionTable').innerHTML = state.memoryCollections.length ? `
    <table class="collection-table">
      <thead><tr><th>Name</th><th>Records</th><th>Vector</th><th>Status</th><th>Actions</th></tr></thead>
      <tbody>
        ${state.memoryCollections.map(collection => `
          <tr>
            <td><strong>${escapeHtml(humanizeCollectionName(collection.name))}</strong><span>${escapeHtml(collection.name)}</span></td>
            <td>${escapeHtml(collection.recordCount)}</td>
            <td>${collection.vectorSize ? `${escapeHtml(collection.vectorSize)}d` : '-'}${collection.distance ? ` · ${escapeHtml(collection.distance)}` : ''}</td>
            <td>${escapeHtml(collection.status ?? collection.provider ?? '-')}</td>
            <td><div class="table-actions"><button data-collection-detail="${escapeHtml(collection.name)}" type="button">Details</button><button data-browse-collection="${escapeHtml(collection.name)}" type="button">Browse</button><button data-delete-collection="${escapeHtml(collection.name)}" type="button">Delete</button></div></td>
          </tr>
        `).join('')}
      </tbody>
    </table>
  ` : '<p class="muted">No memory collections yet.</p>';

  document.querySelectorAll('[data-collection-detail]').forEach(button => {
    button.addEventListener('click', () => showCollectionDetail(button.dataset.collectionDetail));
  });
  document.querySelectorAll('[data-browse-collection]').forEach(button => {
    button.addEventListener('click', () => browseCollection(button.dataset.browseCollection));
  });
  document.querySelectorAll('[data-delete-collection]').forEach(button => {
    button.addEventListener('click', () => deleteCollection(button.dataset.deleteCollection));
  });
}

function humanizeCollectionName(name) {
  return name
    .replace(/[_-]+/g, ' ')
    .replace(/\b\w/g, character => character.toUpperCase());
}

async function showCollectionDetail(name) {
  const detail = await api(`/memory/collections/${encodeURIComponent(name)}`);
  $('collectionInspector').innerHTML = `
    <div class="inspector-head">
      <div><strong>${escapeHtml(humanizeCollectionName(detail.name))}</strong><span>${escapeHtml(detail.name)}</span></div>
    </div>
    <div class="detail-grid">
      <div><span>provider</span><strong>${escapeHtml(detail.provider)}</strong></div>
      <div><span>records</span><strong>${escapeHtml(detail.recordCount)}</strong></div>
      <div><span>status</span><strong>${escapeHtml(detail.status ?? '-')}</strong></div>
      <div><span>vector</span><strong>${detail.vectorSize ? `${escapeHtml(detail.vectorSize)}d` : '-'}${detail.distance ? ` · ${escapeHtml(detail.distance)}` : ''}</strong></div>
    </div>
  `;
}

async function browseCollection(name) {
  await guarded(async () => {
    state.selectedCollection = name;
    state.selectedTenant = null;
    state.selectedCategory = null;
    state.collectionInspectCursor = null;
    state.collectionInspectFilter = {};
    const groups = await api(`/memory/collections/${encodeURIComponent(name)}/groups`);
    renderCollectionTree(groups);
  }, `Browsing ${name}...`, { overlay: false });
}

function renderCollectionTree(groups) {
  $('collectionInspector').innerHTML = `
    <div class="inspector-head">
      <div>
        <strong>${escapeHtml(humanizeCollectionName(groups.collection))}</strong>
        <span>${escapeHtml(groups.sampledRecords)} sampled records grouped by tenant and category</span>
      </div>
    </div>
    <div class="collection-tree">
      ${(groups.tenants ?? []).map(tenant => `
        <details open>
          <summary>${escapeHtml(tenant.tenant)} <span>${escapeHtml(tenant.count)} records</span></summary>
          <div class="tree-children">
            ${(tenant.categories ?? []).map(category => `
              <button data-tree-filter="${escapeHtml(groups.collection)}|${escapeHtml(tenant.tenant)}|${escapeHtml(category.category)}" type="button">
                ${escapeHtml(category.category)} <span>${escapeHtml(category.count)}</span>
              </button>
            `).join('')}
          </div>
        </details>
      `).join('') || '<p class="muted">No tenant/category metadata found in the sampled records.</p>'}
    </div>
    <div id="collectionRecordPane" class="collection-record-pane"><p class="muted">Select a tenant/category to preview records.</p></div>
  `;

  document.querySelectorAll('[data-tree-filter]').forEach(button => {
    button.addEventListener('click', () => {
      const [collection, tenant, category] = button.dataset.treeFilter.split('|');
      inspectCollection(collection, { tenant, category });
    });
  });
}

async function inspectCollection(name, options = {}) {
  await guarded(async () => {
    if (options.reset !== false) {
      state.collectionInspectCursor = null;
    }

    state.selectedCollection = name;
    $('memoryCollectionSelect').value = name;
    state.selectedTenant = options.tenant ?? state.selectedTenant;
    state.selectedCategory = options.category ?? state.selectedCategory;
    const filter = {
      ...(state.selectedTenant && state.selectedTenant !== 'unscoped' ? { tenant: state.selectedTenant } : {}),
      ...(state.selectedCategory && state.selectedCategory !== 'uncategorized' ? { category: state.selectedCategory } : {})
    };
    state.collectionInspectFilter = filter;
    const result = await api(`/memory/collections/${encodeURIComponent(name)}/inspect`, {
      method: 'POST',
      body: JSON.stringify({ limit: 12, cursor: options.cursor ?? state.collectionInspectCursor, filter })
    });

    state.collectionInspectCursor = result.nextCursor;
    renderCollectionInspector(result, options.append === true);
  }, `Inspecting ${name}...`, { overlay: false });
}

function renderCollectionInspector(result, append = false) {
  const target = $('collectionRecordPane') ?? $('collectionInspector');
  const existing = append ? $('collectionRecords')?.innerHTML ?? '' : '';
  target.innerHTML = `
    <div class="record-pane-head">
      <div>
        <strong>${escapeHtml(state.selectedTenant ?? 'All tenants')} / ${escapeHtml(state.selectedCategory ?? 'All categories')}</strong>
        <span>${escapeHtml(result.count)} matching records · vectors hidden</span>
      </div>
    </div>
    <div id="collectionRecords" class="collection-records">
      ${existing}${renderCollectionRecords(result.records)}
    </div>
    ${result.nextCursor ? '<button id="loadMoreCollectionRecords" type="button">Load more</button>' : ''}
  `;

  $('loadMoreCollectionRecords')?.addEventListener('click', () => inspectCollection(result.collection, { reset: false, append: true, cursor: result.nextCursor }));
}

function renderCollectionRecords(records) {
  return records.map(record => {
    const metadata = Object.entries(record.metadata ?? {})
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([key, value]) => `<span>${escapeHtml(key)}=${escapeHtml(value)}</span>`)
      .join('');
    return `
      <article class="collection-record">
        <div class="record-head"><code>${escapeHtml(record.id.slice(0, 12))}</code><span>${escapeHtml(record.textLength)} chars</span></div>
        <p>${escapeHtml(record.textPreview || '(empty text)')}</p>
        <div class="metadata-row">${metadata || '<span>no metadata</span>'}</div>
      </article>
    `;
  }).join('') || '<p class="muted">No records matched this filter.</p>';
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

async function openQdrantBrowser() {
  $('qdrantModal').hidden = false;
  await guarded(async () => {
    await loadMemoryCollections();
  }, 'Opening Qdrant browser...', { overlay: false });
}

function closeQdrantBrowser() {
  $('qdrantModal').hidden = true;
  $('collectionInspector').innerHTML = '';
  state.selectedTenant = null;
  state.selectedCategory = null;
}

async function deleteCollection(name) {
  if (!confirm(`Delete memory collection ${name}? This removes its vectors from the active store.`)) return;
  await guarded(async () => {
    await api(`/memory/collections/${encodeURIComponent(name)}`, { method: 'DELETE' });
    await Promise.all([loadMemoryStats(), loadMemoryCollections()]);
  }, 'Deleting collection...');
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
  const recent = state.backgroundJobs.filter(job => job.sessionId === state.sessionId).slice(0, 8);
  $('backgroundJobs').innerHTML = recent.map(job => {
    const total = job.progressTotal || 0;
    const current = job.progressCurrent || 0;
    const percent = total > 0 ? Math.round((current / total) * 100) : 0;
    const canCancel = job.status === 'queued' || job.status === 'running';
    return `
      <div class="background-job ${escapeHtml(job.status)}">
        <div class="job-title"><strong>${escapeHtml(job.title ?? job.kind)}</strong><span>${escapeHtml(job.status)}</span></div>
        <progress value="${escapeHtml(String(current))}" max="${escapeHtml(String(Math.max(total, current, 1)))}"></progress>
        <span>${escapeHtml(total > 0 ? `${current}/${total} · ${percent}%` : 'waiting')}</span>
        <p>${escapeHtml(job.statusMessage ?? '')}</p>
        ${job.error ? `<em>${escapeHtml(job.error)}</em>` : ''}
        <div class="job-artifacts" data-job-artifacts="${escapeHtml(job.id)}"></div>
        ${canCancel ? `<button data-cancel-job="${escapeHtml(job.id)}" type="button">Cancel</button>` : ''}
      </div>
    `;
  }).join('') || '<p class="muted">No background jobs for this session.</p>';

  document.querySelectorAll('[data-cancel-job]').forEach(button => {
    button.addEventListener('click', () => cancelBackgroundJob(button.dataset.cancelJob));
  });

  recent.filter(job => job.status === 'completed').forEach(job => loadJobArtifacts(job.id).catch(() => {}));
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
  container.innerHTML = artifacts.map(artifact => `
    <button data-view-artifact="${escapeHtml(jobId)}:${escapeHtml(artifact.id)}" type="button">${escapeHtml(artifact.title)}</button>
  `).join('');
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
    <div class="job-title"><strong>Artifact Preview</strong><button id="closeArtifact" type="button">Close</button></div>
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
    model: $('modelSelect').value || null,
    createdAt: now,
    updatedAt: now,
    messages: []
  };
  state.sessionId = null;
  knownJobStates = new Map();
  $('sessionTitle').textContent = state.draftSession.title;
  renderMessages([]);
  state.selectedTraceId = null;
  renderTaskGraph(null, 'Select an assistant message to inspect its task graph.');
  renderSteps([]);
  await Promise.all([loadSessions(), loadMemoryStats()]);
  await Promise.all([loadTaskGraph(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadMcpServers()]);
  renderRuntime(await api('/health'));
}

async function deleteAllSessions() {
  await guarded(async () => {
    await api('/sessions', { method: 'DELETE' });
    state.sessionId = null;
    $('sessionTitle').textContent = 'Ready';
    renderMessages([]);
    renderTaskGraph(null, 'Select an assistant message to inspect its task graph.');
    renderSteps([]);
    await Promise.all([loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs()]);
    await newSession();
  }, 'Deleting sessions...');
}

async function openSession(id) {
  const session = await api(`/sessions/${id}`);
  state.draftSession = null;
  state.sessionId = session.id;
  knownJobStates = new Map();
  $('sessionTitle').textContent = session.title;
  $('modelSelect').value = session.model ?? '';
  state.selectedTraceId = null;
  renderMessages(session.messages ?? []);
  await Promise.all([loadSessions(), loadTaskGraph(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadMcpServers()]);
  renderRuntime(await api('/health'));
}

async function ensurePersistedSession() {
  if (state.sessionId) return;
  const session = await api('/sessions', {
    method: 'POST',
    body: JSON.stringify({
      title: state.draftSession?.title ?? 'LLLMax session',
      agent: state.draftSession?.agent ?? 'coordinator',
      model: $('modelSelect').value || state.draftSession?.model || null
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
  const message = $('prompt').value.trim();
  if (!message) return;

  await guarded(async () => {
    await ensurePersistedSession();
    $('prompt').value = '';
    $('sendButton').disabled = true;
    const existingMessages = documentMessages();
    renderMessages([...existingMessages, { role: 'user', content: message }, { role: 'assistant', content: '' }]);
    setInlineProgress('Routing request...');

    const result = await streamChat(`/sessions/${state.sessionId}/chat/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        message,
        model: $('modelSelect').value || null,
        reasoningEffort: effortMap[$('effort').value],
        allowTools: $('allowTools').checked,
        persistToMemory: $('persistMemory').checked
      })
    });

    renderMessages(result.messages);
    renderMetrics(result.metrics);
    const assistantTrace = [...state.messageTraces.entries()].at(-1);

    if (assistantTrace) {
      selectMessageTrace(assistantTrace[0]);
    }
    await Promise.all([loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadMcpServers(), loadTools()]);
  }, 'Thinking...', { overlay: false }).finally(() => {
    clearInlineProgress();
    $('sendButton').disabled = false;
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
        setInlineProgress(event.content);
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
      }

      if (event.type === 'error') {
        throw new Error(event.content ?? 'Streaming chat failed.');
      }
    }
  }

  if (!finalResult) {
    throw new Error('Streaming chat ended without a final response.');
  }

  return finalResult;
}

function parseServerEvent(raw) {
  const dataLine = raw.split('\n').find(line => line.startsWith('data: '));
  return dataLine ? JSON.parse(dataLine.slice(6)) : null;
}

function appendAssistantChunk(content) {
  const messages = $('messages');
  const assistantMessages = messages.querySelectorAll('.message.assistant');
  const target = assistantMessages[assistantMessages.length - 1];

  if (!target) return;

  target.dataset.raw = (target.dataset.raw ?? '') + content;
  target.querySelector('.markdown-body').innerHTML = renderMarkdown(target.dataset.raw);
  messages.scrollTop = messages.scrollHeight;
}

function setInlineProgress(content) {
  const messages = $('messages');
  let progress = $('inlineProgress');

  if (!progress) {
    progress = document.createElement('div');
    progress.id = 'inlineProgress';
    progress.className = 'inline-progress';
    progress.innerHTML = '<span class="inline-spinner"></span><span class="inline-progress-text"></span>';
    messages.appendChild(progress);
  }

  progress.querySelector('.inline-progress-text').textContent = content || 'Thinking...';
  messages.scrollTop = messages.scrollHeight;
}

function clearInlineProgress() {
  $('inlineProgress')?.remove();
}

function documentMessages() {
  return [...document.querySelectorAll('.message')].map(element => ({
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

async function searchMemory() {
  await guarded(async () => {
    const query = $('memoryQuery').value.trim();
    if (!query) return;
    const collection = $('memoryCollectionSelect').value || 'coordinator';
    state.selectedCollection = collection;
    const filter = parseMetadataFilter($('collectionFilter')?.value ?? '');
    const result = await api('/memory/search', {
      method: 'POST',
      body: JSON.stringify({ collection, query, limit: 8, filter })
    });
    renderMemorySearchResults(collection, query, result);
  }, 'Searching memory...');
}

function renderMemorySearchResults(collection, query, results) {
  $('memoryResults').innerHTML = `
    <div class="search-summary"><strong>${escapeHtml(collection)}</strong><span>${escapeHtml(results.length)} semantic matches for ${escapeHtml(query)}</span></div>
    <div class="collection-records">
      ${results.map(result => {
        const metadata = Object.entries(result.metadata ?? {})
          .sort(([left], [right]) => left.localeCompare(right))
          .map(([key, value]) => `<span>${escapeHtml(key)}=${escapeHtml(value)}</span>`)
          .join('');
        return `
          <article class="collection-record">
            <div class="record-head"><code>${escapeHtml(result.id.slice(0, 12))}</code><span>score ${escapeHtml(Number(result.score).toFixed(3))}</span></div>
            <p>${escapeHtml(result.text || '(empty text)')}</p>
            <div class="metadata-row">${metadata || '<span>no metadata</span>'}</div>
          </article>
        `;
      }).join('') || '<p class="muted">No semantic matches.</p>'}
    </div>
  `;
}

async function consolidateSession() {
  await guarded(async () => {
    if (!state.sessionId) throw new Error('Open a session first.');
    await api('/memory/consolidation', {
      method: 'POST',
      body: JSON.stringify({ sessionId: state.sessionId })
    });
    await Promise.all([loadMemoryStats(), loadConsolidationJobs()]);
  }, 'Consolidating memory...');
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
    await Promise.all([loadApprovals(), loadTools()]);
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
$('uploadDocument').addEventListener('click', uploadDocument);
$('runOcr').addEventListener('click', () => runDocumentAction('/documents/ocr'));
$('extractInvoice').addEventListener('click', () => runDocumentAction('/documents/extract-invoice'));
$('discoverApi').addEventListener('click', discoverApi);
$('searchMemory').addEventListener('click', searchMemory);
$('consolidateSession').addEventListener('click', consolidateSession);
$('openQdrantBrowser').addEventListener('click', openQdrantBrowser);
$('closeQdrantBrowser').addEventListener('click', closeQdrantBrowser);
$('qdrantModal').addEventListener('click', event => {
  if (event.target.id === 'qdrantModal') closeQdrantBrowser();
});
$('registerMcp').addEventListener('click', registerMcp);

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
}

renderMetrics(null);
renderToolTraces([]);
renderCitations([]);
renderSteps([]);

await guarded(async () => {
  await Promise.all([loadModels(), loadAgents(), loadTools(), loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadMcpServers()]);
  renderRuntime(await api('/health'));
  await newSession();
}, 'Bootstrapping local runtime...');

setInterval(() => {
  if (document.visibilityState === 'visible') {
    loadBackgroundJobs().catch(() => {});
  }
}, 3000);
