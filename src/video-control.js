(action, seconds) => {
  const candidates = [];
  const visit = root => {
    for (const element of root.querySelectorAll('*')) {
      if (element.matches('video, bwp-video') && typeof element.play === 'function' &&
          typeof element.pause === 'function') candidates.push(element);
      if (element.shadowRoot) visit(element.shadowRoot);
      if (element.tagName === 'IFRAME') {
        try { if (element.contentDocument) visit(element.contentDocument); } catch (_) { /* Cross-origin frame. */ }
      }
    }
  };
  visit(document);
  const visible = candidates.filter(video => {
    const rect = video.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && getComputedStyle(video).visibility !== 'hidden';
  }).sort((a, b) => {
    const area = video => { const rect = video.getBoundingClientRect(); return rect.width * rect.height; };
    return area(b) - area(a);
  });
  const video = visible[0];
  if (!video) return { message: '当前页面未找到可控制的视频，请先打开视频页面。' };
  try {
    if (action === 'toggle') {
      if (!video.paused) {
        video.pause();
        return { message: '已暂停' };
      }
      const playing = video.play();
      if (playing && typeof playing.catch === 'function') playing.catch(() => {
        // Only sends an error notification; no native APIs are exposed to the website.
        window.chrome.webview.postMessage({ type: 'floatscreen-play-error' });
      });
      return { message: '已请求播放' };
    }
    let start = 0, end = video.duration;
    if (video.seekable && video.seekable.length) {
      start = video.seekable.start(0);
      end = video.seekable.end(video.seekable.length - 1);
    }
    if (!Number.isFinite(end) || end <= start)
      return { message: '当前视频尚未加载或不支持跳转。' };
    const delta = action === 'backward' ? -seconds : seconds;
    video.currentTime = Math.max(start, Math.min(end, video.currentTime + delta));
    return { message: (delta < 0 ? '向后跳转 ' : '向前跳转 ') + seconds + ' 秒' };
  } catch (_) {
    return { message: '视频操作未能完成，请先点击网页播放器后重试。' };
  }
}
