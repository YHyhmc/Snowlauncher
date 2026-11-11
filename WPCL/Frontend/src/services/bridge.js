const pending = new Map();
const listeners = new Set();

function handleMessage(event) {
  const message = event.data;
  if (message?.type === "response") {
    const request = pending.get(message.id);
    if (!request) return;
    pending.delete(message.id);
    if (message.ok) request.resolve(message.data);
    else request.reject(new Error(message.error || "操作失败。"));
    return;
  }

  if (message?.type === "event") {
    listeners.forEach(listener => listener(message.event, message.data));
  }
}

window.chrome?.webview?.addEventListener("message", handleMessage);

export const bridge = {
  request(action, payload = null) {
    if (!window.chrome?.webview) {
      return Promise.reject(new Error("WebView2 消息桥不可用。"));
    }

    const id = Date.now() + "-" + Math.random().toString(16).slice(2);
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject });
      window.chrome.webview.postMessage({ id, action, payload });
    });
  },

  subscribe(listener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }
};
