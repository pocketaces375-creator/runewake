#!/usr/bin/env python3
import socket, socketserver, os, sys

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 9999
APK = os.path.expanduser("~/runewake/client/exports/Runewake.apk")

class Handler(socketserver.StreamRequestHandler):
    def handle(self):
        fsize = os.path.getsize(APK)
        resp = (
            "HTTP/1.1 200 OK\r\n"
            "Content-Type: application/vnd.android.package-archive\r\n"
            f"Content-Disposition: attachment; filename=\"Runewake.apk\"\r\n"
            f"Content-Length: {fsize}\r\n"
            "Accept-Ranges: bytes\r\n"
            "Connection: keep-alive\r\n"
            "\r\n"
        )
        self.wfile.write(resp.encode())
        with open(APK, 'rb') as f:
            while True:
                chunk = f.read(65536)
                if not chunk:
                    break
                self.wfile.write(chunk)
        print(f"Served {APK} ({fsize} bytes)")

s = socket.socket(socket.AF_INET6, socket.SOCK_STREAM)
s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.bind(('::', PORT))
s.listen(5)
print(f"Server ready on http://[::]:{PORT}/")
while True:
    conn, addr = s.accept()
    print(f"Connection from {addr}")
    Handler(conn, ('::', PORT), None)