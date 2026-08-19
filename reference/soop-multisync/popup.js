const addAllButton = document.getElementById('addAll');
const toggleButton = document.getElementById('toggle');
const safetyInput = document.getElementById('safety');
const safetyText = document.getElementById('safetyText');
const membersNode = document.getElementById('members');
const noticeNode = document.getElementById('notice');
const countNode = document.getElementById('count');
const availableNode = document.getElementById('available');
let state = { enabled: false, safetyMs: 7000, members: [] };

function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, (char) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
  }[char]));
}

function seconds(value) {
  return Number.isFinite(value) ? `${value.toFixed(2)}초` : '--';
}

function sourceLabel(source) {
  return source === 'multiview' ? '멀티뷰' : '일반 탭';
}

function render(nextState) {
  if (nextState?.error) {
    noticeNode.textContent = nextState.error;
    return;
  }
  if (nextState) state = nextState;
  safetyInput.value = String(state.safetyMs);
  safetyText.textContent = `${(state.safetyMs / 1000).toFixed(1)}초`;
  toggleButton.textContent = state.enabled ? '정지' : '시작';
  toggleButton.classList.toggle('on', state.enabled);
  countNode.textContent = `${state.members.length}/4`;
  noticeNode.textContent = state.members.length < 2 ? '방송을 2개 이상 등록해주세요.' : '';

  if (!state.members.length) {
    membersNode.innerHTML = '<div class="empty">위의 감지 목록에서<br>싱크를 맞출 방송을 추가해주세요.</div>';
    return;
  }

  membersNode.innerHTML = state.members.map((member) => {
    const timeline = member.timeline;
    const status = member.status;
    const error = member.syncErrorMs;
    const errorClass = Number.isFinite(error) && Math.abs(error) < 300 ? 'good' : 'warn';
    return `
      <article class="member">
        <div>
          <h2 title="${escapeHtml(member.title)}">${escapeHtml(member.title)} <small>· ${sourceLabel(member.source)}</small></h2>
          <div class="stats">
            <span>${timeline ? `CDN ${timeline.source}` : 'CDN 탐색 중'}</span>
            <span>버퍼 ${seconds(status?.bufferSec)}</span>
            <span class="${errorClass}">오차 ${Number.isFinite(error) ? `${(error / 1000).toFixed(2)}초` : '--'}</span>
            <span>${status ? `${Number(status.playbackRate).toFixed(2)}x` : '--'}</span>
          </div>
        </div>
        <button class="remove" data-member-key="${escapeHtml(member.key)}">제거</button>
      </article>`;
  }).join('');

  document.querySelectorAll('.remove').forEach((button) => {
    button.addEventListener('click', async () => {
      render(await chrome.runtime.sendMessage({ type: 'REMOVE_STREAM', key: button.dataset.memberKey }));
      refreshAvailable();
    });
  });
}

function candidateMarkup(stream) {
  const label = sourceLabel(stream.source);
  return `
    <div class="candidate">
      <span class="candidate-title" title="${escapeHtml(stream.title)}">
        ${escapeHtml(stream.title)} <small>· ${label}</small>
      </span>
      <button data-add-key="${escapeHtml(stream.key)}" class="${stream.joined ? 'joined' : ''}"
        ${stream.joined || state.members.length >= 4 ? 'disabled' : ''}>
        ${stream.joined ? '등록됨' : '추가'}
      </button>
    </div>`;
}

async function refreshAvailable() {
  const result = await chrome.runtime.sendMessage({ type: 'LIST_SOOP_STREAMS' });
  const streams = result?.streams || [];
  if (!streams.length) {
    availableNode.innerHTML = '<div class="available-empty">SOOP 플레이어를 찾는 중입니다.<br>방송 탭을 새로고침해 주세요.</div>';
    addAllButton.disabled = true;
    return;
  }

  const tabStreams = streams.filter((stream) => stream.source === 'tab');
  const multiviewStreams = streams.filter((stream) => stream.source === 'multiview');
  const sections = [];
  if (tabStreams.length) {
    sections.push(`<div class="candidate-group"><b>일반 탭</b>${tabStreams.map(candidateMarkup).join('')}</div>`);
  }
  if (multiviewStreams.length) {
    sections.push(`<div class="candidate-group"><b>SOOP Kit 멀티뷰</b>${multiviewStreams.map(candidateMarkup).join('')}</div>`);
  }
  availableNode.innerHTML = sections.join('');
  addAllButton.disabled = streams.every((stream) => stream.joined) || state.members.length >= 4;

  document.querySelectorAll('[data-add-key]').forEach((button) => {
    if (button.disabled) return;
    button.addEventListener('click', async () => {
      render(await chrome.runtime.sendMessage({ type: 'ADD_STREAM', key: button.dataset.addKey }));
      refreshAvailable();
    });
  });
}

addAllButton.addEventListener('click', async () => {
  render(await chrome.runtime.sendMessage({ type: 'ADD_ALL_STREAMS' }));
  refreshAvailable();
});

toggleButton.addEventListener('click', async () => {
  render(await chrome.runtime.sendMessage({ type: 'SET_ENABLED', enabled: !state.enabled }));
});

safetyInput.addEventListener('input', () => {
  safetyText.textContent = `${(Number(safetyInput.value) / 1000).toFixed(1)}초`;
});

safetyInput.addEventListener('change', async () => {
  render(await chrome.runtime.sendMessage({ type: 'SET_SAFETY', safetyMs: Number(safetyInput.value) }));
});

chrome.runtime.onMessage.addListener((message) => {
  if (message.type === 'GROUP_STATE') {
    render(message.state);
  }
});

chrome.runtime.sendMessage({ type: 'GET_STATE' }).then((initialState) => {
  render(initialState);
  refreshAvailable();
});
