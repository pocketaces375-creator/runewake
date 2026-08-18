#!/usr/bin/env python3
"""Simple HTTP server to serve the Runewake APK for direct download."""
import http.server
import os
import socket

APK_PATH = "/home/fictive/runewake/client/exports/Runewake.apk"
APK_SIZE = os.path.getsize(APK_PATH)
PORT = 9099

class APKServer(http.server.HTTPServer):
    allow_reuse_address = True

class APKHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/Runewake.apk":
            self.send_response(200)
            self.send_header("Content-Type", "application/vnd.android.package-archive")
            self.send_header("Content-Disposition", 'attachment; filename="Runewake.apk"')
            self.send_header("Content-Length", str(APK_SIZE))
            self.end_headers()
            with open(APK_PATH, "rb") as f:
                self.wfile.write(f.read())
            return
        # Root page
        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        self.end_headers()
        self.wfile.write(b"<html><body>")
        self.wfile.write(b'<a href="/Runewake.apk">Download Runewake.apk</a>')
        self.wfile.write(f" ({APK_SIZE // 1024 // 1024}MB)".encode())
        self.wfile.write(b"</body></html>")

    def log_message(self, fmt, *args):
        print(fmt % args, flush=True)

# Get local IP
s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
s.connect(("8.8.8.8", 80))
local_ip = s.getsockname()[0]
s.close()

server = APKServer(("0.0.0.0", PORT), APKHandler)
print(f"SERVING: http://{local_ip}:{PORT}/Runewake.apk", flush=True)
print(f"PORT: {PORT}", flush=True)
server.serve_forever()