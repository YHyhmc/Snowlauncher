namespace WPCL.Services;

internal static class OAuthCompletionPage
{
    public const string Success = """
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>Snow launcher 授权完成</title>
        </head>
        <body style="margin:0;min-height:100vh;display:grid;place-items:center;background:#F8F8F8;color:#111213;font-family:'Segoe UI','Microsoft YaHei',sans-serif">
          <main style="width:min(420px,calc(100vw - 48px));padding:34px;border:1px solid #DBDADB;border-radius:6px;background:#FBFEFD;box-shadow:0 18px 50px rgba(17,18,19,.12);text-align:center">
            <div style="display:grid;width:52px;height:52px;margin:0 auto 20px;place-items:center;border-radius:50%;background:rgba(42,151,242,.12);color:#2A97F2">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg>
            </div>
            <div style="color:#2A97F2;font-size:11px;font-weight:700;letter-spacing:.12em">SNOW LAUNCHER</div>
            <h1 style="margin:8px 0 9px;font-size:24px">Microsoft 授权完成</h1>
            <p style="margin:0;color:#797986;font-size:14px;line-height:1.7">启动器正在继续验证 Xbox 与 Minecraft 账户。最终结果请以启动器提示为准。</p>
            <button type="button" onclick="window.close()" style="height:40px;margin-top:24px;padding:0 18px;border:0;border-radius:6px;background:#2A97F2;color:#FBFEFD;font-weight:700;cursor:pointer">关闭页面</button>
          </main>
        </body>
        </html>
        """;

    public const string Error = """
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>Snow launcher 登录失败</title>
        </head>
        <body style="margin:0;min-height:100vh;display:grid;place-items:center;background:#F8F8F8;color:#111213;font-family:'Segoe UI','Microsoft YaHei',sans-serif">
          <main style="width:min(420px,calc(100vw - 48px));padding:34px;border:1px solid #DBDADB;border-radius:6px;background:#FBFEFD;box-shadow:0 18px 50px rgba(17,18,19,.12);text-align:center">
            <div style="display:grid;width:52px;height:52px;margin:0 auto 20px;place-items:center;border-radius:50%;background:rgba(255,75,75,.12);color:#FF4B4B">
              <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M7 7l10 10M17 7 7 17"/></svg>
            </div>
            <div style="color:#FF4B4B;font-size:11px;font-weight:700;letter-spacing:.12em">SNOW LAUNCHER</div>
            <h1 style="margin:8px 0 9px;font-size:24px">未能完成登录</h1>
            <p style="margin:0;color:#797986;font-size:14px;line-height:1.7">请返回启动器查看具体原因，然后重新添加账户。</p>
            <button type="button" onclick="window.close()" style="height:40px;margin-top:24px;padding:0 18px;border:0;border-radius:6px;background:#111213;color:#FBFEFD;font-weight:700;cursor:pointer">返回启动器</button>
          </main>
        </body>
        </html>
        """;
}
