import os
import sys
import time
import socket
import threading
import shutil
import tempfile
import urllib.request
import webview
from werkzeug.serving import make_server
from app import app, UPLOAD_DIR

class ServerThread(threading.Thread):
    def __init__(self, host, port, wsgi_app):
        super().__init__()
        self.server = make_server(host, port, wsgi_app)
        self.ctx = app.app_context()
        self.ctx.push()
        self.daemon = True

    def run(self):
        self.server.serve_forever()

    def shutdown(self):
        self.server.shutdown()

def find_free_port(start_port=5055):
    """Find an open port starting from start_port."""
    for port in range(start_port, start_port + 100):
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
                s.bind(('127.0.0.1', port))
                return port
        except OSError:
            continue
    # Fallback to ephemeral port
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(('', 0))
        return s.getsockname()[1]

def wait_for_server(url, timeout=10.0):
    start = time.time()
    while time.time() - start < timeout:
        try:
            with urllib.request.urlopen(url) as response:
                if response.status == 200:
                    return True
        except Exception:
            time.sleep(0.1)
    return False

def cleanup_temp():
    """Clean temporary uploads upon exit."""
    try:
        if os.path.exists(UPLOAD_DIR):
            shutil.rmtree(UPLOAD_DIR, ignore_errors=True)
    except Exception:
        pass

def main():
    port = find_free_port(5055)
    host = "127.0.0.1"
    server_url = f"http://{host}:{port}"

    # Start Flask server
    server_thread = ServerThread(host, port, app)
    server_thread.start()

    # Wait for server to be responsive
    wait_for_server(server_url)

    # Determine icon path if available
    base_dir = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    icon_path = os.path.join(base_dir, "app_icon.ico")
    if not os.path.exists(icon_path):
        icon_path = None

    # Create native Windows desktop window using Edge WebView2
    window = webview.create_window(
        title="Print Studio",
        url=server_url,
        width=1280,
        height=860,
        min_size=(980, 650),
        text_select=True,
        confirm_close=False
    )

    try:
        # Start GUI event loop
        webview.start(debug=False)
    finally:
        # User closed window: Clean shutdown
        try:
            server_thread.shutdown()
        except Exception:
            pass
        cleanup_temp()
        sys.exit(0)

if __name__ == "__main__":
    main()
