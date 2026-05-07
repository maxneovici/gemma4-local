const state = {
  sessionId: null,
  uploadedDocumentId: null,
  models: [],
  sessions: [],
  tools: [],
  agents: [],
  taskGraph: null,
  backgroundJobs: [],
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
  $('messages').innerHTML = messages.filter(message => message.role !== 'progress').map(renderMessage).join('');
  $('messages').scrollTop = $('messages').scrollHeight;
}

function renderMessage(message) {
  const role = message.role === 'user' ? 'user' : 'assistant';
  const raw = message.content ?? '';

  if (role === 'user') {
    return `<div class="message user" data-raw="${escapeHtml(raw)}">${escapeHtml(raw)}</div>`;
  }

  return `<div class="message assistant" data-raw="${escapeHtml(raw)}"><div class="markdown-body">${renderMarkdown(raw)}</div></div>`;
}

function renderRuntime(health) {
  const highModel = state.models.find(model => model.name === 'gemma4:31b');
  $('runtime').innerHTML = [
    ['ollama', health?.isHealthy ? 'healthy' : 'unknown'],
    ['models', state.models.length],
    ['31b', highModel ? `${highModel.sizeGb} GB` : 'missing'],
    ['session', state.sessionId ? 'active' : 'none']
  ].map(([label, value]) => `<div class="runtime-pill"><span>${escapeHtml(label)}</span><strong>${escapeHtml(String(value))}</strong></div>`).join('');
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
    ? steps.map(step => `<div class="step"><strong>${escapeHtml(step.kind)}</strong>\n${escapeHtml(step.content)}</div>`).join('')
    : '<p class="muted">Reasoning steps appear after a run.</p>';
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
  state.sessions = await api('/sessions');
  $('sessions').innerHTML = state.sessions.map(session => `
    <button type="button" class="${session.id === state.sessionId ? 'active' : ''}" data-session="${session.id}">
      ${escapeHtml(session.title)}<br><small>${session.messageCount} messages</small>
    </button>
  `).join('');

  document.querySelectorAll('[data-session]').forEach(button => {
    button.addEventListener('click', () => guarded(() => openSession(button.dataset.session), 'Opening session...'));
  });
}

async function loadMemoryStats() {
  const stats = await api('/memory/stats');
  $('memoryStats').innerHTML = `
    <div class="runtime-pill"><span>provider</span><strong>${escapeHtml(stats.provider)}</strong></div>
    <div class="runtime-pill"><span>collections</span><strong>${stats.collectionCount}</strong></div>
    <div class="runtime-pill"><span>records</span><strong>${stats.recordCount}</strong></div>
  `;
}

async function loadConsolidationJobs() {
  state.consolidationJobs = await api('/memory/consolidation/jobs');
  $('consolidationJobs').innerHTML = state.consolidationJobs.slice(0, 3).map(job => `
    <div class="job ${escapeHtml(job.status)}"><strong>${escapeHtml(job.status)}</strong><span>${escapeHtml(job.sessionId.slice(0, 8))} · ${escapeHtml(String(job.memoriesWritten))} memories</span></div>
  `).join('') || '<p class="muted">No consolidation jobs yet.</p>';
}

async function loadBackgroundJobs() {
  state.backgroundJobs = await api('/background-jobs');
  await refreshCurrentSessionIfJobsCompleted(state.backgroundJobs);
  const recent = state.backgroundJobs.slice(0, 8);
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
  }).join('') || '<p class="muted">No background jobs yet.</p>';

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
    <a href="/background-jobs/${escapeHtml(jobId)}/artifacts/${escapeHtml(artifact.id)}" target="_blank" rel="noreferrer">${escapeHtml(artifact.title)}</a>
  `).join('');
}

async function loadTaskGraph() {
  if (!state.sessionId) {
    renderTaskGraph(null);
    return;
  }

  const response = await fetch(`/task-graphs/sessions/${state.sessionId}`);
  state.taskGraph = response.ok ? await response.json() : null;
  renderTaskGraph(state.taskGraph);
}

function renderTaskGraph(graph) {
  if (!graph) {
    $('taskGraph').innerHTML = '<p class="muted">No task graph for this session yet.</p>';
    return;
  }

  const active = graph.nodes?.find(node => node.id === graph.activeNodeId);
  const artifacts = graph.artifacts ?? [];
  const events = graph.events ?? [];
  $('taskGraph').innerHTML = `
    <div class="graph-head"><strong>${escapeHtml(graph.status)}</strong><span>${escapeHtml(Math.round((graph.confidence ?? 0) * 100))}% confidence</span></div>
    <p>${escapeHtml(graph.goal)}</p>
    <div class="active-node">active: ${escapeHtml(active?.title ?? 'none')}</div>
    <div class="node-list">
      ${(graph.nodes ?? []).map(node => `
        <div class="node ${escapeHtml(node.status)}">
          <strong>${escapeHtml(node.title)}</strong>
          <span>${escapeHtml(node.kind ?? 'turn')} · ${escapeHtml(node.status)} · ${escapeHtml(Math.round((node.confidence ?? 0) * 100))}%</span>
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
  const session = await api('/sessions', { method: 'POST', body: JSON.stringify({ title: 'LLLMax session', model: $('modelSelect').value || null }) });
  state.sessionId = session.id;
  $('sessionTitle').textContent = session.title;
  renderMessages(session.messages ?? []);
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
    renderTaskGraph(null);
    await Promise.all([loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs()]);
    await newSession();
  }, 'Deleting sessions...');
}

async function openSession(id) {
  const session = await api(`/sessions/${id}`);
  state.sessionId = session.id;
  $('sessionTitle').textContent = session.title;
  $('modelSelect').value = session.model ?? '';
  renderMessages(session.messages ?? []);
  await Promise.all([loadSessions(), loadTaskGraph(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadMcpServers()]);
  renderRuntime(await api('/health'));
}

async function sendMessage(event) {
  event.preventDefault();
  const message = $('prompt').value.trim();
  if (!message) return;

  await guarded(async () => {
    if (!state.sessionId) await newSession();
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
    renderSteps(result.reasoningSteps);
    await Promise.all([loadSessions(), loadMemoryStats(), loadTaskGraph(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadMcpServers(), loadTools()]);
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
          renderTaskGraph(state.taskGraph);
        }
      }

      if (event.type === 'task_graph' && event.payload) {
        state.taskGraph = event.payload;
        renderTaskGraph(state.taskGraph);
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
    const result = await api('/memory/search', {
      method: 'POST',
      body: JSON.stringify({ collection: 'coordinator', query, limit: 5 })
    });
    $('memoryResults').textContent = JSON.stringify(result, null, 2);
  }, 'Searching memory...');
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
$('registerMcp').addEventListener('click', registerMcp);

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
}

renderMetrics(null);
renderSteps([]);

await guarded(async () => {
  await Promise.all([loadModels(), loadAgents(), loadTools(), loadSessions(), loadMemoryStats(), loadConsolidationJobs(), loadBackgroundJobs(), loadApprovals(), loadMcpServers()]);
  renderRuntime(await api('/health'));

  if (state.sessions[0]) {
    await openSession(state.sessions[0].id);
  } else {
    await newSession();
  }
}, 'Bootstrapping local runtime...');

setInterval(() => {
  if (document.visibilityState === 'visible') {
    loadBackgroundJobs().catch(() => {});
  }
}, 3000);
