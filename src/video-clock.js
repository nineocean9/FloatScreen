() => {
  if (!/(^|\.)bilibili\.com$/i.test(location.hostname)) return { available: false };
  const candidates = [];
  const visit = root => {
    for (const element of root.querySelectorAll('*')) {
      if (element.matches('video, bwp-video') && typeof element.pause === 'function') candidates.push(element);
      if (element.shadowRoot) visit(element.shadowRoot);
      if (element.tagName === 'IFRAME') {
        try { if (element.contentDocument) visit(element.contentDocument); } catch (_) { }
      }
    }
  };
  const cached = window.__floatscreenClockVideo;
  if (cached?.isConnected && areaOf(cached) > 0) candidates.push(cached);
  else {
    for (const element of document.querySelectorAll('video,bwp-video'))
      if (typeof element.pause === 'function') candidates.push(element);
    if (!candidates.length) visit(document);
  }
  function areaOf(element) { const r = element.getBoundingClientRect(); return r.width * r.height; }
  const area = element => { const r = element.getBoundingClientRect(); return r.width * r.height; };
  const video = candidates.filter(element => area(element) > 0).sort((a, b) => area(b) - area(a))[0];
  if (video) window.__floatscreenClockVideo = video;
  const url = new URL(location.href);
  const bvid = (url.pathname.match(/BV[0-9A-Za-z]{10}/) || [url.searchParams.get('bvid') || ''])[0];
  const part = Math.max(1, parseInt(url.searchParams.get('p') || '1', 10) || 1);
  if (!video || !/^BV[0-9A-Za-z]{10}$/.test(bvid) || !Number.isFinite(Number(video.currentTime)))
    return { available: false };
  let cid = 0;
  const info = window.__INITIAL_STATE__?.videoData;
  if (info?.bvid === bvid) {
    const page = info.pages?.find(item => Number(item.page) === part);
    cid = Number(page?.cid || (part === 1 ? info.cid : 0)) || 0;
  }
  return {
    available: true, bvid, part, cid: String(cid), time: Number(video.currentTime),
    paused: Boolean(video.paused), seeking: Boolean(video.seeking),
    buffering: Number(video.readyState) < 3 && !video.paused,
    rate: Number(video.playbackRate) || 1
  };
}
