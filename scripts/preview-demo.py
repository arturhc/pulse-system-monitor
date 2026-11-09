"""Serve the actual frontend with synthetic data. Never accesses hardware or app data.

Run: python scripts/preview-demo.py
Open: http://127.0.0.1:8765
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parent.parent
UI = ROOT / "ui"
BRIDGE = r"""
const listeners=[];
window.chrome??={};
window.__demoEmit=data=>listeners.forEach(f=>f({data}));
window.chrome.webview={addEventListener:(_,fn)=>listeners.push(fn),postMessage:cmd=>{
 if(cmd.startsWith('view:')){
  window.__demoEmit({type:'prefs',expanded:true,pinned:false,startup:false});
  window.__demoEmit({type:'view',view:cmd.slice(5)});
 }
 if(cmd.startsWith('history:'))window.__demoEmit({type:'history',data:{range:cmd.slice(8),count:0,points:[],batteryDaily:[],bucketMs:120000}});
 if(cmd==='diagnostics'||cmd==='admin')window.__demoEmit({type:'notice',message:'Vista de demostración. No consulta hardware, instala software ni escribe historiales.'});
}};
"""
FEED = r"""
(()=>{const s=__DEMO_SNAPSHOT__;
 document.querySelector('.edition').textContent='VISTA DEMO';
 const emit=()=>window.__demoEmit({type:'sample',data:{...s,time:new Date().toISOString()}});
 window.__demoEmit({type:'prefs',expanded:false,pinned:false,startup:false});
 for(let i=0;i<30;i++){
  window.__demoEmit({type:'sample',data:{...s,cpu:24+Math.sin(i*.7)*5,gpu:16+Math.cos(i*.5)*3,time:new Date().toISOString()}});
 }
 emit();setInterval(emit,2000);
})();
"""


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = self.path.split("?", 1)[0]
        assets = {
            "/": ("text/html; charset=utf-8", (UI / "index.html").read_text(encoding="utf-8").replace('<script src="app.js">', '<script src="demo-bridge.js"></script><script src="app.js">').replace("</body>", '<script src="demo-feed.js"></script></body>')),
            "/demo-bridge.js": ("application/javascript", BRIDGE),
            "/demo-feed.js": ("application/javascript", FEED.replace("__DEMO_SNAPSHOT__", (ROOT / "docs/demo-snapshot.json").read_text(encoding="utf-8"))),
            "/demo-snapshot.json": ("application/json", (ROOT / "docs/demo-snapshot.json").read_text(encoding="utf-8")),
        }
        for name in ("style.css", "accents.css", "features.css", "app.js", "features.js"):
            assets["/" + name] = ("text/css" if name.endswith(".css") else "application/javascript", (UI / name).read_text(encoding="utf-8"))
        if path not in assets:
            self.send_error(404)
            return
        mime, text = assets[path]
        body = text.encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", mime)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    print("Pulse demo: http://127.0.0.1:8765 (synthetic data only)", flush=True)
    ThreadingHTTPServer(("127.0.0.1", 8765), Handler).serve_forever()
