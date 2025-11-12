// ==== CONFIGURATION ====
const CONFIG = {
  API_BASE: 'http://localhost:5272',
  TRANSACTION_API_BASE: 'http://localhost:5032',
  LIMIT: 1000,
  POLL_INTERVAL: 10000,
  REVIEWABLE_STATUSES: new Set(['DECIDED_BLOCK', 'DECIDED_REVIEW', 'REQUESTED'])
};

// ==== STATE ====
const state = {
  list: [],
  byId: new Map(),
  limit: CONFIG.LIMIT,
  connectionStatus: 'Đang kết nối...',
  lastRenderedCount: 0,
  selectedReviewId: null,
  submittingReview: false,
  reviewCloseTimer: null,
  mode: 'live',
  searchQuery: null,
  connection: null
};

let pollTimer = null;

// ==== DOM ELEMENTS ====
const dom = {
  rows: document.getElementById('rows'),
  stat: document.getElementById('stat'),
  modeBadge: document.getElementById('modeBadge'),
  apiBaseLbl: document.getElementById('apiBaseLbl'),
  limitSpan: document.getElementById('limit'),
  filter: document.getElementById('filter'),
  reviewPanel: document.getElementById('reviewPanel'),
  reviewTxId: document.getElementById('reviewTxId'),
  reviewStatus: document.getElementById('reviewStatus'),
  reviewAmount: document.getElementById('reviewAmount'),
  reviewAccounts: document.getElementById('reviewAccounts'),
  reviewForm: document.getElementById('reviewForm'),
  reviewSubmit: document.getElementById('reviewSubmit'),
  reviewCancel: document.getElementById('reviewCancel'),
  reviewFeedback: document.getElementById('reviewFeedback'),
  searchForm: document.getElementById('searchForm'),
  qStatus: document.getElementById('qStatus'),
  qFrom: document.getElementById('qFrom'),
  qTo: document.getElementById('qTo'),
  qStart: document.getElementById('qStart'),
  qEnd: document.getElementById('qEnd'),
  qSize: document.getElementById('qSize'),
  btnGoLive: document.getElementById('btnGoLive'),
  btnClear: document.getElementById('btnClear'),
  searchInfo: document.getElementById('searchInfo')
};

// ==== UTILITY FUNCTIONS ====
const formatMoney = value => 
  value == null ? '' : Number(value).toLocaleString(undefined, { maximumFractionDigits: 2 });

const formatTime = iso => 
  iso ? new Date(iso).toLocaleString() : '';

const toDecisionFromStatus = status => {
  if (!status) return '';
  if (status.startsWith('DECIDED_')) return status.replace('DECIDED_', '');
  if (status.startsWith('REVIEWED_')) return status.replace('REVIEWED_', '');
  if (status === 'LEDGER_APPLIED') return 'ALLOW';
  return '';
};

const buildTransactionApi = path => {
  const base = CONFIG.TRANSACTION_API_BASE.endsWith('/') 
    ? CONFIG.TRANSACTION_API_BASE 
    : `${CONFIG.TRANSACTION_API_BASE}/`;
  return new URL(path.replace(/^\/+/, ''), base).toString();
};

const toIso = dateTimeLocalValue => {
  if (!dateTimeLocalValue) return null;
  const d = new Date(dateTimeLocalValue);
  return d.toISOString();
};

// ==== STATE MANAGEMENT ====
function updateModeBadge() {
  dom.modeBadge.textContent = state.mode === 'live' ? 'Live' : 'Kết quả tìm kiếm';
}

function updateStat(displayedCount) {
  const current = displayedCount ?? state.lastRenderedCount ?? state.list.length;
  const modeStr = state.mode === 'live'
    ? `Realtime: ${state.connectionStatus}`
    : `Kết quả tĩnh từ /api/transactions/search${state.searchQuery ? ' — ' + state.searchQuery : ''}`;
  dom.stat.textContent = `Tổng: ${state.list.length} | Đang hiển thị: ${current} | ${modeStr}`;
}

function setConnectionStatus(text) {
  state.connectionStatus = text;
  updateStat();
}

function upsert(doc) {
  const id = doc.transactionId;
  if (!id) return;
  
  const prev = state.byId.get(id) || {};
  const merged = { ...prev, ...doc };
  
  if (!merged.createdAtUtc && prev.createdAtUtc) {
    merged.createdAtUtc = prev.createdAtUtc;
  }
  
  state.byId.set(id, merged);
  state.list = Array.from(state.byId.values())
    .sort((a, b) => new Date(b.createdAtUtc || 0) - new Date(a.createdAtUtc || 0))
    .slice(0, state.limit);
  
  render();
}

// ==== RENDERING ====
function render() {
  const q = dom.filter.value.trim().toLowerCase();

  const items = q
    ? state.list.filter(x => {
        const st = (x.status || '').toLowerCase();
        const dec = (x.decision || toDecisionFromStatus(x.status) || '').toLowerCase();
        return (x.transactionId || '').toLowerCase().includes(q)
          || (x.fromAccount || '').toLowerCase().includes(q)
          || (x.toAccount || '').toLowerCase().includes(q)
          || st.includes(q)
          || dec.includes(q);
      })
    : state.list;

  if (items.length === 0) {
    dom.rows.innerHTML = `
      <tr>
        <td colspan="9" style="text-align:center; padding:32px; color:var(--gray-500)">
          Không có kết quả phù hợp.
        </td>
      </tr>
    `;
    state.lastRenderedCount = 0;
    updateStat(0);
    refreshReviewPanel();
    return;
  }

  dom.rows.innerHTML = items.map(x => {
    const rawStatus = x.status || '';
    const normalizedStatus = rawStatus.toUpperCase();
    const cls = 'chip status-' + normalizedStatus.replace(/[^A-Z_]/g, '');
    const decision = x.decision || toDecisionFromStatus(normalizedStatus) || '';
    const actionCell = CONFIG.REVIEWABLE_STATUSES.has(normalizedStatus)
      ? `<div class="actions-cell">
           <button type="button" class="btn btn-primary" data-review="${x.transactionId}">
             Đánh giá
           </button>
         </div>`
      : '';
    
    return `
      <tr>
        <td>${formatTime(x.createdAtUtc)}</td>
        <td class="mono">${x.transactionId || ''}</td>
        <td>${x.fromAccount || ''}</td>
        <td>${x.toAccount || ''}</td>
        <td class="text-right mono">${formatMoney(x.amount)}</td>
        <td><span class="${cls}">${rawStatus}</span></td>
        <td class="text-right mono">${x.risk == null ? '' : Number(x.risk).toFixed(3)}</td>
        <td>${decision}</td>
        <td class="text-center">${actionCell}</td>
      </tr>
    `;
  }).join('');

  state.lastRenderedCount = items.length;
  updateStat(items.length);
  refreshReviewPanel();
}

// ==== REVIEW PANEL ====
function refreshReviewPanel() {
  if (!state.selectedReviewId) return;
  
  const tx = state.byId.get(state.selectedReviewId);
  if (!tx) {
    closeReviewPanel();
    return;
  }

  const normalizedStatus = (tx.status || '').toUpperCase();
  
  if (!CONFIG.REVIEWABLE_STATUSES.has(normalizedStatus) && normalizedStatus.startsWith('REVIEWED_')) {
    updateReviewPanel(tx);
    scheduleReviewPanelClose();
    return;
  }

  if (!CONFIG.REVIEWABLE_STATUSES.has(normalizedStatus)) {
    closeReviewPanel();
    return;
  }
  
  updateReviewPanel(tx);
}

function updateReviewPanel(tx) {
  dom.reviewTxId.textContent = tx.transactionId ?? '';
  dom.reviewStatus.textContent = tx.status ?? '';
  dom.reviewAmount.textContent = formatMoney(tx.amount);
  dom.reviewAccounts.textContent = `${tx.fromAccount ?? ''} → ${tx.toAccount ?? ''}`;
}

function clearReviewPanelCloseTimer() {
  if (state.reviewCloseTimer) {
    clearTimeout(state.reviewCloseTimer);
    state.reviewCloseTimer = null;
  }
}

function scheduleReviewPanelClose() {
  clearReviewPanelCloseTimer();
  state.reviewCloseTimer = setTimeout(() => {
    state.reviewCloseTimer = null;
    closeReviewPanel();
  }, 1500);
}

function openReviewPanel(tx) {
  state.selectedReviewId = tx.transactionId;
  clearReviewPanelCloseTimer();
  dom.reviewForm.reset();
  
  const reviewerInput = dom.reviewForm.elements.reviewedBy;
  const noteInput = dom.reviewForm.elements.note;
  const actionInput = dom.reviewForm.elements.action;
  
  if (reviewerInput) reviewerInput.value = '';
  if (noteInput) noteInput.value = '';
  if (actionInput) actionInput.value = 'APPROVE';
  
  setReviewFeedback('info', '');
  updateReviewPanel(tx);
  dom.reviewPanel.classList.remove('hidden');
  
  if (reviewerInput && typeof reviewerInput.focus === 'function') {
    setTimeout(() => reviewerInput.focus(), 100);
  }
}

function closeReviewPanel() {
  clearReviewPanelCloseTimer();
  state.selectedReviewId = null;
  dom.reviewForm.reset();
  dom.reviewPanel.classList.add('hidden');
  setReviewFeedback('info', '');
  setReviewSubmitting(false);
}

function setReviewSubmitting(isSubmitting) {
  state.submittingReview = isSubmitting;
  dom.reviewSubmit.disabled = isSubmitting;
  dom.reviewCancel.disabled = isSubmitting;
  
  const elements = Array.from(dom.reviewForm.elements);
  elements.forEach(el => {
    if (el.name === 'action' || el.name === 'reviewedBy' || el.name === 'note') {
      el.disabled = isSubmitting;
    }
  });
}

function setReviewFeedback(kind, message) {
  dom.reviewFeedback.textContent = message || '';
  dom.reviewFeedback.classList.toggle('success', kind === 'success');
  dom.reviewFeedback.classList.toggle('error', kind === 'error');
}

async function submitReview(event) {
  event.preventDefault();
  
  if (!state.selectedReviewId) {
    setReviewFeedback('error', 'Vui lòng chọn giao dịch cần duyệt.');
    return;
  }

  const action = dom.reviewForm.elements.action.value;
  const reviewedBy = (dom.reviewForm.elements.reviewedBy.value || '').trim();
  const note = (dom.reviewForm.elements.note.value || '').trim();
  
  if (!reviewedBy) {
    setReviewFeedback('error', 'Tên người duyệt không được để trống.');
    dom.reviewForm.elements.reviewedBy.focus();
    return;
  }

  let endpoint;
  try {
    endpoint = buildTransactionApi(
      `/api/transactions/${encodeURIComponent(state.selectedReviewId)}/review`
    );
  } catch (err) {
    setReviewFeedback('error', err instanceof Error ? err.message : String(err));
    return;
  }

  const payload = {
    action,
    reviewedBy,
    note: note || null
  };

  try {
    setReviewSubmitting(true);
    setReviewFeedback('info', 'Đang gửi kết quả duyệt...');
    
    const res = await fetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    
    const text = await res.text();
    let json;
    try {
      json = text ? JSON.parse(text) : {};
    } catch {
      json = { raw: text };
    }
    
    if (!res.ok) {
      const errorMessage = json?.error || json?.message || `HTTP ${res.status}`;
      throw new Error(errorMessage);
    }

    const status = typeof json?.status === 'string' 
      ? json.status 
      : (action === 'APPROVE' ? 'REVIEWED_APPROVE' : 'REVIEWED_REJECT');
    const decision = action === 'APPROVE' ? 'APPROVE' : 'REJECT';
    
    upsert({
      transactionId: state.selectedReviewId,
      status,
      decision
    });
    
    setReviewFeedback('success', 'Đã cập nhật kết quả duyệt.');
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    setReviewFeedback('error', message);
  } finally {
    setReviewSubmitting(false);
  }
}

// ==== SEARCH FUNCTIONALITY ====
function buildSearchQueryString() {
  const params = new URLSearchParams();
  
  if (dom.qStatus.value) params.set('status', dom.qStatus.value);
  if (dom.qFrom.value.trim()) params.set('fromAccount', dom.qFrom.value.trim());
  if (dom.qTo.value.trim()) params.set('toAccount', dom.qTo.value.trim());
  
  const startIso = toIso(dom.qStart.value);
  if (startIso) params.set('startDate', startIso);
  
  const endIso = toIso(dom.qEnd.value);
  if (endIso) params.set('endDate', endIso);
  
  const size = parseInt(dom.qSize.value || '100', 10);
  if (size) params.set('size', String(size));
  
  return params.toString();
}

function querySummary() {
  const parts = [];
  
  if (dom.qStatus.value) parts.push(`status=${dom.qStatus.value}`);
  if (dom.qFrom.value) parts.push(`from=${dom.qFrom.value}`);
  if (dom.qTo.value) parts.push(`to=${dom.qTo.value}`);
  if (dom.qStart.value) parts.push(`start=${dom.qStart.value}`);
  if (dom.qEnd.value) parts.push(`end=${dom.qEnd.value}`);
  if (dom.qSize.value && dom.qSize.value !== '100') parts.push(`size=${dom.qSize.value}`);
  
  return parts.join(' • ');
}

async function executeSearch() {
  const qs = buildSearchQueryString();
  const url = `${CONFIG.API_BASE}/api/transactions/search${qs ? ('?' + qs) : ''}`;
  
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status} @ ${url}`);
  
  const data = await res.json();
  const hits = (data.hits && data.hits.hits) ? data.hits.hits : [];

  state.byId.clear();
  state.list = [];
  render();
  
  hits.forEach(h => {
    const s = h._source || {};
    upsert({
      transactionId: s.transactionId || h._id,
      status: s.status,
      amount: s.amount,
      fromAccount: s.fromAccount,
      toAccount: s.toAccount,
      risk: s.risk,
      decision: s.decision,
      createdAtUtc: s.createdAtUtc
    });
  });
  
  state.mode = 'search';
  state.searchQuery = querySummary();
  updateModeBadge();
  dom.searchInfo.textContent = state.searchQuery 
    ? `Đang xem kết quả với bộ lọc: ${state.searchQuery}` 
    : 'Đang xem kết quả tìm kiếm';
  updateStat();
}

// ==== DATA LOADING ====
async function loadLatest() {
  const latestUrl = `${CONFIG.API_BASE}/api/transactions/latest?size=${state.limit}`;
  const res = await fetch(latestUrl);
  
  if (!res.ok) throw new Error(`HTTP ${res.status} @ ${latestUrl}`);
  
  const data = await res.json();
  const hits = (data.hits && data.hits.hits) ? data.hits.hits : [];

  if (hits.length === 0) {
    state.mode = 'search';
    state.searchQuery = querySummary();
    updateModeBadge();
    dom.searchInfo.textContent = 'Không có kết quả phù hợp.';
    updateStat(0);
    render();
    return;
  }
  
  state.byId.clear();
  state.list = [];
  render();
  
  hits.forEach(h => {
    const s = h._source || {};
    upsert({
      transactionId: s.transactionId || h._id,
      status: s.status,
      amount: s.amount,
      fromAccount: s.fromAccount,
      toAccount: s.toAccount,
      risk: s.risk,
      decision: s.decision,
      createdAtUtc: s.createdAtUtc
    });
  });
}

// ==== POLLING ====
function ensurePolling() {
  if (pollTimer) return;
  pollTimer = setInterval(() => {
    if (state.mode === 'live') {
      refresh().catch(() => {});
    }
  }, CONFIG.POLL_INTERVAL);
}

function stopPolling() {
  if (!pollTimer) return;
  clearInterval(pollTimer);
  pollTimer = null;
}

async function refresh() {
  await loadLatest();
}

// ==== SIGNALR ====
async function initSignalR() {
  if (!window.signalR || !signalR.HubConnectionBuilder) {
    setConnectionStatus('SignalR client không sẵn sàng — dùng polling');
    ensurePolling();
    await refresh();
    return;
  }

  setConnectionStatus('Đang kết nối SignalR...');
  
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${CONFIG.API_BASE}/hubs/transactions`, {
      skipNegotiation: true,
      transport: signalR.HttpTransportType.WebSockets,
      withCredentials: false
    })
    .withAutomaticReconnect()
    .build();

  state.connection = connection;

  connection.on('txEvent', e => {
    if (state.mode !== 'live') return;
    stopPolling();
    
    try {
      upsert({
        transactionId: e.transactionId ?? e.TransactionId,
        status: e.status ?? e.Status,
        decision: e.decision ?? e.Decision,
        risk: e.risk ?? e.Risk,
        amount: e.amount ?? e.Amount,
        fromAccount: e.fromAccount ?? e.FromAccount,
        toAccount: e.toAccount ?? e.ToAccount,
        createdAtUtc: e.createdAtUtc ?? e.CreatedAtUtc
      });
    } catch (err) {
      console.error('Không xử lý được thông điệp SignalR', err);
    }
  });

  connection.onreconnecting(() => {
    setConnectionStatus('Đang khôi phục kết nối...');
    ensurePolling();
  });

  connection.onreconnected(() => {
    setConnectionStatus('Đã kết nối SignalR');
    stopPolling();
    if (state.mode === 'live') {
      refresh().catch(() => {});
    }
  });

  connection.onclose(() => {
    setConnectionStatus('Đã ngắt kết nối — sẽ thử lại trong 5s');
    ensurePolling();
    setTimeout(initSignalR, 5000);
  });

  async function start() {
    try {
      await connection.start();
      setConnectionStatus('Đã kết nối SignalR');
      stopPolling();
    } catch (err) {
      console.error('SignalR start fail', err);
      setConnectionStatus('Lỗi kết nối — thử lại trong 5s');
      ensurePolling();
      setTimeout(start, 5000);
    }
  }
  
  await start();
}

// ==== EVENT LISTENERS ====
function setupEventListeners() {
  // Quick filter
  dom.filter.addEventListener('input', render);

  // Review panel actions
  document.getElementById('grid').addEventListener('click', ev => {
    const target = ev.target.closest('button[data-review]');
    if (!target) return;
    
    const id = target.dataset.review;
    if (!id) return;
    
    const tx = state.byId.get(id);
    if (!tx) {
      setReviewFeedback('error', 'Không tìm thấy giao dịch trong bộ nhớ tạm.');
      return;
    }
    
    openReviewPanel(tx);
  });

  dom.reviewCancel.addEventListener('click', () => {
    if (state.submittingReview) return;
    closeReviewPanel();
  });

  dom.reviewForm.addEventListener('submit', submitReview);

  // Search form
  dom.searchForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    try {
      await executeSearch();
    } catch (err) {
      console.error(err);
      dom.searchInfo.textContent = (err && err.message) ? err.message : 'Lỗi tìm kiếm';
    }
  });

  dom.btnClear.addEventListener('click', () => {
    dom.qStatus.value = '';
    dom.qFrom.value = '';
    dom.qTo.value = '';
    dom.qStart.value = '';
    dom.qEnd.value = '';
    dom.qSize.value = '100';
    dom.searchInfo.textContent = '';
  });

  dom.btnGoLive.addEventListener('click', async () => {
    if (state.mode === 'live') {
      await refresh().catch(() => {});
      return;
    }
    
    state.mode = 'live';
    state.searchQuery = null;
    updateModeBadge();
    dom.searchInfo.textContent = 'Đang quay về realtime...';
    await refresh().catch(() => {});
    dom.searchInfo.textContent = '';
    updateStat();
  });
}

// ==== INITIALIZATION ====
function init() {
  // Set initial UI values
  dom.limitSpan.textContent = state.limit;
  dom.apiBaseLbl.textContent = CONFIG.API_BASE;
  updateModeBadge();
  updateStat(0);

  // Setup event listeners
  setupEventListeners();

  // Bootstrap data loading
  (async () => {
    try {
      await loadLatest();
    } catch (e) {
      console.error('Failed to load initial data:', e);
    }
    await initSignalR();
  })();
}

// Start the application
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', init);
} else {
  init();
}