(() => {
  'use strict';

  let joined = false;
  let applyingRate = false;
  let overlay = null;
  let statusPanel = null;
  let overlayTimer = 0;
  let lastBufferEventAt = 0;
  let resumeTimer = 0;

  function videos() {
    return [...document.querySelectorAll('video')];
  }

  function identity() {
    const streamId = location.pathname.split('/').filter(Boolean)[0] || '';
    const visibleName = document.querySelector(
      '.broadcast_information .nickname, .broadcast_info .nickname, .bj_name, [class*="nickname"]'
    )?.textContent?.trim();
    const titleName = document.title.split(' - ')[0]?.trim();
    const usableTitle = titleName || visibleName;
    const title = usableTitle && usableTitle !== 'SOOP' && usableTitle !== 'embed'
      ? usableTitle
      : streamId;
    return { streamId, title };
  }

  function primaryVideo() {
    return videos().sort((a, b) =>
      (b.clientWidth * b.clientHeight) - (a.clientWidth * a.clientHeight)
    )[0] || null;
  }

  function ranges(range) {
    const result = [];
    for (let i = 0; i < range.length; i += 1) result.push([range.start(i), range.end(i)]);
    return result;
  }

  function status() {
    const video = primaryVideo();
    if (!video) return { hasVideo: false, lastBufferEventAt };
    const buffered = ranges(video.buffered);
    const seekable = ranges(video.seekable);
    const bufferEnd = buffered.length ? buffered.at(-1)[1] : null;
    return {
      hasVideo: true,
      currentTime: video.currentTime,
      playbackRate: video.playbackRate,
      paused: video.paused,
      readyState: video.readyState,
      buffering: video.readyState < 3 && !video.paused,
      bufferSec: bufferEnd === null ? null : Math.max(0, bufferEnd - video.currentTime),
      buffered,
      seekable,
      lastBufferEventAt
    };
  }

  function ensureOverlay() {
    if (overlay?.isConnected) return overlay;
    overlay = document.createElement('div');
    overlay.id = 'soop-multisync-status';
    overlay.style.cssText = [
      'position:fixed', 'top:14px', 'left:50%', 'transform:translateX(-50%)',
      'z-index:2147483647', 'padding:6px 10px', 'border-radius:999px',
      'background:rgba(18,20,24,.88)', 'color:#fff',
      'font:700 12px/1.2 system-ui,sans-serif', 'pointer-events:none',
      'box-shadow:0 4px 16px rgba(0,0,0,.3)'
    ].join(';');
    document.documentElement.appendChild(overlay);
    return overlay;
  }

  function ensureStatusPanel() {
    const rightControls = document.querySelector('#player .player_ctrlBox .ctrlBox .right_ctrl');
    if (!statusPanel) {
      statusPanel = document.createElement('div');
      statusPanel.id = 'soop-multisync-player-status';
      statusPanel.style.cssText = [
        'display:none', 'align-items:center', 'gap:5px', 'height:26px',
        'margin-right:8px', 'padding:0 8px', 'border-radius:6px',
        'background:rgba(14,18,23,.82)', 'border:1px solid rgba(255,255,255,.14)',
        'color:#dce4ec', 'font:700 11px/1 system-ui,sans-serif',
        'white-space:nowrap', 'pointer-events:none', 'z-index:2147483647',
        'text-shadow:0 1px 2px rgba(0,0,0,.8)'
      ].join(';');
    }

    if (rightControls && statusPanel.parentElement !== rightControls) {
      statusPanel.style.position = 'static';
      statusPanel.style.left = '';
      statusPanel.style.bottom = '';
      statusPanel.style.transform = '';
      rightControls.insertBefore(statusPanel, rightControls.firstChild);
    } else if (!rightControls && !statusPanel.isConnected) {
      statusPanel.style.position = 'fixed';
      statusPanel.style.left = '50%';
      statusPanel.style.bottom = '58px';
      statusPanel.style.transform = 'translateX(-50%)';
      document.documentElement.appendChild(statusPanel);
    }
    return statusPanel;
  }

  function renderStatusPanel(reply, currentStatus) {
    const panel = ensureStatusPanel();
    panel.style.display = joined ? 'flex' : 'none';
    if (!joined) return;

    const timeline = reply?.member?.timeline;
    const errorMs = reply?.member?.syncErrorMs;
    const cdnText = timeline
      ? timeline.source === 'program-date-time' ? 'CDN 절대시각' : 'CDN 추정'
      : 'CDN 탐색중';
    const bufferText = Number.isFinite(currentStatus.bufferSec)
      ? `버퍼 ${currentStatus.bufferSec.toFixed(2)}초`
      : '버퍼 --';
    const errorText = Number.isFinite(errorMs)
      ? `오차 ${(errorMs / 1000).toFixed(2)}초`
      : '오차 --';
    const rateText = Number.isFinite(currentStatus.playbackRate)
      ? `${currentStatus.playbackRate.toFixed(2)}x`
      : '--';

    const cdnColor = timeline ? '#62d49b' : '#91a0ae';
    const errorColor = !Number.isFinite(errorMs)
      ? '#ffcb6b'
      : Math.abs(errorMs) < 300 ? '#62d49b' : '#ffcb6b';
    panel.innerHTML = [
      `<span style="color:${cdnColor}">${cdnText}</span>`,
      `<span style="color:#66727f">·</span>`,
      `<span>${bufferText}</span>`,
      `<span style="color:#66727f">·</span>`,
      `<span style="color:${errorColor}">${errorText}</span>`,
      `<span style="color:#66727f">·</span>`,
      `<span style="color:#8bc7ff">${rateText}</span>`
    ].join('');
  }

  function hideOverlay() {
    clearTimeout(overlayTimer);
    if (overlay) overlay.style.display = 'none';
  }

  function renderOverlay(text, active = true, durationMs = 1200) {
    const node = ensureOverlay();
    node.textContent = text;
    node.style.display = joined && text ? 'block' : 'none';
    node.style.background = active ? 'rgba(31,122,78,.92)' : 'rgba(70,74,82,.9)';
    clearTimeout(overlayTimer);
    if (durationMs > 0) {
      overlayTimer = setTimeout(() => {
        node.style.display = 'none';
      }, durationMs);
    }
  }

  function setRate(rate) {
    const video = primaryVideo();
    if (!video || Math.abs(video.playbackRate - rate) < 0.005) return;
    applyingRate = true;
    video.playbackRate = rate;
    setTimeout(() => { applyingRate = false; }, 0);
    renderOverlay(`MultiSync · ${rate.toFixed(2)}x`);
  }

  async function hardAlign(mediaTime) {
    const video = primaryVideo();
    if (!video || !Number.isFinite(mediaTime)) return false;
    const seekable = ranges(video.seekable);
    const valid = seekable.some(([start, end]) => mediaTime >= start && mediaTime <= end);
    if (!valid) return false;
    video.currentTime = mediaTime;
    if (video.paused) await video.play().catch(() => {});
    setRate(1);
    renderOverlay('MultiSync · 재정렬');
    return true;
  }

  function attach(video) {
    if (!video || video.dataset.multiSyncAttached === '1') return;
    video.dataset.multiSyncAttached = '1';
    for (const eventName of ['waiting', 'stalled', 'error']) {
      video.addEventListener(eventName, () => {
        if (!joined || video.paused || video.currentTime < 1) return;
        lastBufferEventAt = Date.now();
        renderOverlay('MultiSync · 버퍼 복구 중', false);
      }, true);
    }
    video.addEventListener('ratechange', () => {
      if (joined && !applyingRate && Math.abs(video.playbackRate - 1) > 0.1) {
        renderOverlay(`사용자 배속 ${video.playbackRate.toFixed(2)}x`, false);
      }
    }, true);
  }

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message.type === 'SET_JOINED') {
      joined = Boolean(message.joined);
      if (joined) renderOverlay('MultiSync · 등록됨');
      else hideOverlay();
      sendResponse(status());
      return true;
    }
    if (message.type === 'SET_SYNC_RATE') {
      setRate(Number(message.rate) || 1);
      sendResponse(status());
      return true;
    }
    if (message.type === 'HARD_ALIGN') {
      hardAlign(Number(message.mediaTime)).then((ok) => sendResponse({ ok, ...status() }));
      return true;
    }
    if (message.type === 'PAUSE_FOR_RESYNC') {
      const video = primaryVideo();
      if (video) {
        video.pause();
        clearTimeout(resumeTimer);
        resumeTimer = setTimeout(() => video.play().catch(() => {}), 850);
      }
      sendResponse(status());
      return true;
    }
    if (message.type === 'GET_CONTENT_STATUS') {
      sendResponse(status());
      return true;
    }
    return false;
  });

  setInterval(() => {
    const video = primaryVideo();
    attach(video);
    const currentStatus = status();
    chrome.runtime.sendMessage({ type: 'STATUS', status: currentStatus, identity: identity() }).then((reply) => {
      if (reply && typeof reply.joined === 'boolean') joined = reply.joined;
      renderStatusPanel(reply, currentStatus);
      if (!joined) hideOverlay();
    }).catch(() => {});
  }, 500);

  chrome.runtime.sendMessage({ type: 'FRAME_HELLO', identity: identity(), status: status() }).catch(() => {});
})();
