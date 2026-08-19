const CONFIG = {
  maxMembers: 4,
  initialSafetyMs: 7000,
  minSafetyMs: 1500,
  maxSafetyMs: 20000,
  reduceStepMs: 100,
  stableWindowMs: 30000,
  coordinateIntervalMs: 450,
  softErrorMs: 180,
  hardErrorMs: 900,
  recoveryStepMs: 1200,
  candidateFreshMs: 5000
};

const group = {
  enabled: false,
  members: new Map(),
  safetyMs: CONFIG.initialSafetyMs,
  stableSince: Date.now(),
  lastCoordinateAt: 0,
  lastBufferEvents: new Map(),
  evidence: new Map()
};

const candidates = new Map();
const requestHeaders = new Map();

function memberKey(tabId, frameId = 0) {
  return `${Number(tabId)}:${Number(frameId)}`;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function headerValue(headers, name) {
  const found = (headers || []).find((header) => header.name.toLowerCase() === name.toLowerCase());
  return found ? found.value : null;
}

function isPlaylistUrl(url) {
  return /\.m3u8(?:\?|$)/i.test(url || '');
}

function parsePlaylist(text) {
  const lines = text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  const firstTimestampLine = lines.find((line) => line.startsWith('#EXT-X-FIRST-SEGMENT-TIMESTAMP:'));
  const sequenceLine = lines.find((line) => line.startsWith('#EXT-X-MEDIA-SEQUENCE:'));
  const pdtLine = lines.find((line) => line.startsWith('#EXT-X-PROGRAM-DATE-TIME:'));
  const durations = lines
    .filter((line) => line.startsWith('#EXTINF:'))
    .map((line) => Number(line.slice(8).split(',')[0]))
    .filter(Number.isFinite);
  if (!firstTimestampLine || !durations.length) return null;

  const firstPtsSec = Number(firstTimestampLine.split(':')[1]) / 10000000;
  if (!Number.isFinite(firstPtsSec)) return null;
  return {
    firstPtsSec,
    edgePtsSec: firstPtsSec + durations.reduce((sum, value) => sum + value, 0),
    segmentDurationSec: durations.reduce((sum, value) => sum + value, 0) / durations.length,
    mediaSequence: sequenceLine ? Number(sequenceLine.split(':')[1]) : null,
    programDateTimeMs: pdtLine ? Date.parse(pdtLine.slice(pdtLine.indexOf(':') + 1)) : null
  };
}

function resolveRequestMember(tabId, frameId) {
  const exact = group.members.get(memberKey(tabId, frameId));
  if (exact) return exact;
  const sameTab = [...group.members.values()].filter((member) => member.tabId === tabId);
  return sameTab.length === 1 ? sameTab[0] : null;
}

async function updateTimeline(key, url, dateHeader) {
  const member = group.members.get(key);
  if (!member) return;
  try {
    const response = await fetch(url, { credentials: 'include', cache: 'no-store' });
    if (!response.ok) return;
    const parsed = parsePlaylist(await response.text());
    if (!parsed) return;
    const serverDateMs = parsed.programDateTimeMs || Date.parse(dateHeader || response.headers.get('date'));
    if (!Number.isFinite(serverDateMs)) return;

    const rawOffsetMs = serverDateMs - parsed.edgePtsSec * 1000;
    const previous = member.timeline;
    const offsetMs = previous && Math.abs(rawOffsetMs - previous.offsetMs) < 8000
      ? previous.offsetMs * 0.8 + rawOffsetMs * 0.2
      : rawOffsetMs;
    member.timeline = {
      ...parsed,
      offsetMs,
      edgeUtcMs: parsed.edgePtsSec * 1000 + offsetMs,
      observedAt: Date.now(),
      source: parsed.programDateTimeMs ? 'program-date-time' : 'cdn-date',
      confidence: parsed.programDateTimeMs ? 1 : 0.45
    };
    member.playlistUrl = url;
  } catch {
    // Signed URLs can expire; a later observed request will retry.
  }
}

chrome.webRequest.onHeadersReceived.addListener(
  (details) => {
    if (details.tabId < 0 || !isPlaylistUrl(details.url)) return;
    const member = resolveRequestMember(details.tabId, details.frameId);
    if (!member) return;
    requestHeaders.set(details.requestId, {
      date: headerValue(details.responseHeaders, 'date'),
      key: member.key,
      url: details.url
    });
  },
  { urls: ['https://*/*'], types: ['xmlhttprequest', 'media', 'other'] },
  ['responseHeaders']
);

chrome.webRequest.onCompleted.addListener(
  (details) => {
    const captured = requestHeaders.get(details.requestId);
    requestHeaders.delete(details.requestId);
    if (captured && group.members.has(captured.key)) {
      updateTimeline(captured.key, captured.url, captured.date);
    }
  },
  { urls: ['https://*/*'], types: ['xmlhttprequest', 'media', 'other'] }
);

chrome.webRequest.onErrorOccurred.addListener(
  (details) => requestHeaders.delete(details.requestId),
  { urls: ['https://*/*'] }
);

function publicState() {
  return {
    enabled: group.enabled,
    safetyMs: group.safetyMs,
    members: [...group.members.values()].map((member) => ({
      key: member.key,
      tabId: member.tabId,
      frameId: member.frameId,
      streamId: member.streamId,
      title: member.title,
      source: member.source,
      status: member.status || null,
      timeline: member.timeline || null,
      syncErrorMs: member.syncErrorMs ?? null,
      correctionMs: group.evidence.get(member.key)?.offsetMs || 0
    }))
  };
}

function broadcastState() {
  chrome.runtime.sendMessage({ type: 'GROUP_STATE', state: publicState() }).catch(() => {});
}

async function sendCommand(memberOrKey, command) {
  const member = typeof memberOrKey === 'string' ? group.members.get(memberOrKey) : memberOrKey;
  if (!member) return null;
  try {
    return await chrome.tabs.sendMessage(member.tabId, command, { frameId: member.frameId });
  } catch {
    return null;
  }
}

function currentUtc(member) {
  if (!member.timeline || !member.status) return null;
  const correction = group.evidence.get(member.key)?.offsetMs || 0;
  return member.status.currentTime * 1000 + member.timeline.offsetMs + correction;
}

function targetMediaTime(member, targetUtcMs) {
  const correction = group.evidence.get(member.key)?.offsetMs || 0;
  return (targetUtcMs - member.timeline.offsetMs - correction) / 1000;
}

async function recoverFromBuffering(member) {
  const segmentMs = (member.timeline?.segmentDurationSec || 1) * 1000;
  group.safetyMs = clamp(
    group.safetyMs + Math.max(CONFIG.recoveryStepMs, segmentMs * 0.5),
    CONFIG.minSafetyMs,
    CONFIG.maxSafetyMs
  );
  group.stableSince = Date.now();
  await Promise.all([...group.members.values()].map((item) => sendCommand(item, { type: 'PAUSE_FOR_RESYNC' })));
  setTimeout(() => coordinate(true), 900);
}

async function coordinate(force = false) {
  if (!group.enabled || group.members.size < 2) return;
  const now = Date.now();
  if (!force && now - group.lastCoordinateAt < CONFIG.coordinateIntervalMs) return;
  group.lastCoordinateAt = now;

  const ready = [...group.members.values()].filter((member) =>
    member.status && member.timeline &&
    now - member.status.receivedAt < 3000 &&
    now - member.timeline.observedAt < 15000
  );
  if (ready.length !== group.members.size) return;

  for (const member of ready) {
    const eventAt = member.status.lastBufferEventAt || 0;
    if (eventAt > (group.lastBufferEvents.get(member.key) || 0)) {
      group.lastBufferEvents.set(member.key, eventAt);
      await recoverFromBuffering(member);
      return;
    }
  }

  if (now - group.stableSince >= CONFIG.stableWindowMs) {
    group.safetyMs = clamp(group.safetyMs - CONFIG.reduceStepMs, CONFIG.minSafetyMs, CONFIG.maxSafetyMs);
    group.stableSince = now;
  }

  const commonEdgeUtc = Math.min(...ready.map((member) => member.timeline.edgeUtcMs));
  const targetUtc = commonEdgeUtc - group.safetyMs;
  await Promise.all(ready.map(async (member) => {
    const errorMs = currentUtc(member) - targetUtc;
    member.syncErrorMs = errorMs;
    const mediaTarget = targetMediaTime(member, targetUtc);
    if (Math.abs(errorMs) >= CONFIG.hardErrorMs) {
      await sendCommand(member, { type: 'HARD_ALIGN', mediaTime: mediaTarget });
    } else if (errorMs > CONFIG.softErrorMs) {
      await sendCommand(member, { type: 'SET_SYNC_RATE', rate: 0.97 });
    } else if (errorMs < -CONFIG.softErrorMs) {
      await sendCommand(member, { type: 'SET_SYNC_RATE', rate: 1.04 });
    } else {
      await sendCommand(member, { type: 'SET_SYNC_RATE', rate: 1 });
    }
  }));
  broadcastState();
}

function updateCandidate(sender, identity) {
  if (!sender.tab?.id) return null;
  const frameId = Number(sender.frameId || 0);
  const key = memberKey(sender.tab.id, frameId);
  const source = frameId === 0 ? 'tab' : 'multiview';
  const candidate = {
    key,
    tabId: sender.tab.id,
    frameId,
    streamId: identity?.streamId || '',
    title: identity?.title || identity?.streamId || sender.tab.title || 'SOOP 방송',
    source,
    tabTitle: sender.tab.title || '',
    url: sender.url || '',
    seenAt: Date.now()
  };
  candidates.set(key, candidate);

  const member = group.members.get(key);
  if (member) {
    member.title = candidate.title;
    member.streamId = candidate.streamId;
    member.url = candidate.url;
  }
  return { key, candidate, member };
}

async function listStreams() {
  const now = Date.now();
  const openTabs = new Set((await chrome.tabs.query({})).map((tab) => tab.id));
  for (const [key, candidate] of candidates) {
    if (!openTabs.has(candidate.tabId) || now - candidate.seenAt > CONFIG.candidateFreshMs) {
      candidates.delete(key);
    }
  }
  return [...candidates.values()]
    .map((candidate) => ({ ...candidate, joined: group.members.has(candidate.key) }))
    .sort((a, b) => a.tabId - b.tabId || a.frameId - b.frameId);
}

async function addCandidate(candidate) {
  if (!candidate) throw new Error('방송 플레이어를 찾지 못했습니다.');
  if (!group.members.has(candidate.key) && group.members.size >= CONFIG.maxMembers) {
    throw new Error('최대 4개 방송까지 등록할 수 있습니다.');
  }
  if (!group.members.has(candidate.key)) {
    group.members.set(candidate.key, {
      ...candidate,
      status: null,
      timeline: null,
      syncErrorMs: null
    });
  }
  await sendCommand(candidate.key, { type: 'SET_JOINED', joined: true });
  broadcastState();
}

const handledTypes = new Set([
  'STATUS', 'FRAME_HELLO', 'GET_STATE', 'LIST_SOOP_STREAMS',
  'ADD_STREAM', 'ADD_ALL_STREAMS', 'REMOVE_STREAM',
  'SET_ENABLED', 'SET_SAFETY', 'OFFSET_EVIDENCE'
]);

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message || !handledTypes.has(message.type)) return false;
  (async () => {
    if ((message.type === 'STATUS' || message.type === 'FRAME_HELLO') && sender.tab?.id) {
      const resolved = updateCandidate(sender, message.identity);
      const member = resolved?.member;
      if (member && message.status) {
        member.status = { ...message.status, receivedAt: Date.now() };
        coordinate();
      }
      sendResponse({
        key: resolved?.key,
        joined: Boolean(member),
        enabled: group.enabled,
        safetyMs: group.safetyMs,
        member: member ? {
          timeline: member.timeline || null,
          syncErrorMs: member.syncErrorMs ?? null,
          correctionMs: group.evidence.get(member.key)?.offsetMs || 0
        } : null
      });
      return;
    }
    if (message.type === 'GET_STATE') {
      sendResponse(publicState());
      return;
    }
    if (message.type === 'LIST_SOOP_STREAMS') {
      sendResponse({ streams: await listStreams(), maxMembers: CONFIG.maxMembers });
      return;
    }
    if (message.type === 'ADD_STREAM') {
      await addCandidate(candidates.get(String(message.key)));
      sendResponse(publicState());
      return;
    }
    if (message.type === 'ADD_ALL_STREAMS') {
      const requestedSource = message.source || null;
      const available = (await listStreams()).filter((item) =>
        !item.joined && (!requestedSource || item.source === requestedSource)
      );
      for (const item of available) {
        if (group.members.size >= CONFIG.maxMembers) break;
        await addCandidate(candidates.get(item.key));
      }
      sendResponse(publicState());
      return;
    }
    if (message.type === 'REMOVE_STREAM') {
      const key = String(message.key);
      const member = group.members.get(key);
      group.members.delete(key);
      group.evidence.delete(key);
      group.lastBufferEvents.delete(key);
      if (member) await sendCommand(member, { type: 'SET_JOINED', joined: false });
      sendResponse(publicState());
      broadcastState();
      return;
    }
    if (message.type === 'SET_ENABLED') {
      group.enabled = Boolean(message.enabled);
      group.stableSince = Date.now();
      if (!group.enabled) {
        await Promise.all([...group.members.values()].map((member) =>
          sendCommand(member, { type: 'SET_SYNC_RATE', rate: 1 })
        ));
      } else {
        coordinate(true);
      }
      sendResponse(publicState());
      broadcastState();
      return;
    }
    if (message.type === 'SET_SAFETY') {
      group.safetyMs = clamp(Number(message.safetyMs), CONFIG.minSafetyMs, CONFIG.maxSafetyMs);
      group.stableSince = Date.now();
      coordinate(true);
      sendResponse(publicState());
      return;
    }
    if (message.type === 'OFFSET_EVIDENCE') {
      const key = String(message.key);
      if (group.members.has(key) && Number(message.confidence) >= 0.7) {
        group.evidence.set(key, {
          offsetMs: Number(message.offsetMs) || 0,
          confidence: Number(message.confidence),
          source: message.source || 'analysis',
          observedAt: Date.now()
        });
        coordinate(true);
      }
      sendResponse(publicState());
    }
  })().catch((error) => sendResponse({ error: error.message }));
  return true;
});

chrome.tabs.onRemoved.addListener((tabId) => {
  let changed = false;
  for (const [key, candidate] of candidates) {
    if (candidate.tabId === tabId) candidates.delete(key);
  }
  for (const [key, member] of group.members) {
    if (member.tabId === tabId) {
      group.members.delete(key);
      group.evidence.delete(key);
      group.lastBufferEvents.delete(key);
      changed = true;
    }
  }
  if (changed) broadcastState();
});
