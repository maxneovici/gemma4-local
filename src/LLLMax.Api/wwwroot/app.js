const state = {
  sessionId: null,
  uploadedDocumentId: null,
  models: [],
  sessions: [],
  tools: [],
  agents: []
};

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

  return response.json();
}

async function guarded(action, label = 'working') {
  setStatus(label, 'busy');
  try {
    const result = await action();
    setStatus('Ready', 'ok');
    return result;
  } catch (error) {
    setStatus(error.message, 'error');
    throw error;
  }
}

function setStatus(text, kind = 'ok') {
  $('statusLine').textContent = text;
  $('statusLine').dataset.kind = kind;
}

function renderMessages(messages) {
  $('messages').innerHTML = messages.map(message => `
    <div class="message ${message.role}">${escapeHtml(message.content)}</div>
  `).join('');
  $('messages').scrollTop = $('messages').scrollHeight;
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

async function newSession() {
  const session = await api('/sessions', { method: 'POST', body: JSON.stringify({ title: 'LLLMax session', model: $('modelSelect').value || null }) });
  state.sessionId = session.id;
  $('sessionTitle').textContent = session.title;
  renderMessages(session.messages ?? []);
  await Promise.all([loadSessions(), loadMemoryStats()]);
  renderRuntime(await api('/health'));
}

async function openSession(id) {
  const session = await api(`/sessions/${id}`);
  state.sessionId = session.id;
  $('sessionTitle').textContent = session.title;
  $('modelSelect').value = session.model ?? '';
  renderMessages(session.messages ?? []);
  await loadSessions();
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
    renderMessages([...documentMessages(), { role: 'user', content: message }]);

    const result = await api(`/sessions/${state.sessionId}/chat`, {
      method: 'POST',
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
    await Promise.all([loadSessions(), loadMemoryStats()]);
  }, 'Thinking...').finally(() => $('sendButton').disabled = false);
}

function documentMessages() {
  return [...document.querySelectorAll('.message')].map(element => ({
    role: element.classList.contains('user') ? 'user' : 'assistant',
    content: element.textContent
  }));
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

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

$('newSession').addEventListener('click', () => guarded(newSession, 'Creating session...'));
$('chatForm').addEventListener('submit', sendMessage);
$('effort').addEventListener('input', event => $('effortLabel').textContent = effortMap[event.target.value]);
$('uploadDocument').addEventListener('click', uploadDocument);
$('runOcr').addEventListener('click', () => runDocumentAction('/documents/ocr'));
$('extractInvoice').addEventListener('click', () => runDocumentAction('/documents/extract-invoice'));
$('discoverApi').addEventListener('click', discoverApi);
$('searchMemory').addEventListener('click', searchMemory);

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js');
}

renderMetrics(null);
renderSteps([]);

await guarded(async () => {
  await Promise.all([loadModels(), loadAgents(), loadTools(), loadSessions(), loadMemoryStats()]);
  renderRuntime(await api('/health'));

  if (state.sessions[0]) {
    await openSession(state.sessions[0].id);
  } else {
    await newSession();
  }
}, 'Bootstrapping local runtime...');
