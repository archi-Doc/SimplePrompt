"""Run the native executable against a Unix pseudo-terminal, including redirected output."""

import errno
import fcntl
import os
import pty
import select
import struct
import subprocess
import sys
import termios
import time


def run(executable, mode):
    master, slave = pty.openpty()
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 120, 0, 0))
    arguments = [executable, "--terminal"]
    if mode == "capture":
        arguments.append("--capture")
    process = subprocess.Popen(
        arguments,
        stdin=slave,
        stdout=subprocess.PIPE if mode == "redirect" else slave,
        stderr=subprocess.PIPE,
        env={**os.environ, "TERM": "xterm-256color"},
        start_new_session=True,
    )
    os.close(slave)
    streams = [master, process.stderr.fileno()]
    if process.stdout is not None:
        streams.append(process.stdout.fileno())
    output = {stream: bytearray() for stream in streams}
    query_buffer = b""
    sent_input = False
    deadline = time.monotonic() + 30
    try:
        while streams and time.monotonic() < deadline:
            readable, _, _ = select.select(streams, [], [], 0.1)
            for stream in readable:
                try:
                    data = os.read(stream, 65536)
                except OSError as error:
                    if error.errno != errno.EIO:
                        raise
                    data = b""
                if not data:
                    streams.remove(stream)
                    continue
                output[stream].extend(data)
                if stream == master:
                    query_buffer += data
                    while b"\x1b[6n" in query_buffer:
                        _, query_buffer = query_buffer.split(b"\x1b[6n", 1)
                        os.write(master, b"\x1b[1;1R")
                    query_buffer = query_buffer[-3:]
                if not sent_input and b"READY" in output[process.stderr.fileno()]:
                    # UTF-8 text, left arrow, Delete, insertion, Enter.
                    os.write(master, "日本語😀abc\x1b[D\x1b[3~Z\r".encode())
                    sent_input = True
            if process.poll() is not None and not readable:
                break

        if process.poll() is None:
            raise TimeoutError(f"{mode}: terminal test timed out")
        combined = b"".join(output.values()).decode(errors="replace")
        if process.returncode != 0 or "NativeAOT smoke test passed." not in combined:
            raise AssertionError(f"{mode}: exit {process.returncode}\n{combined}")
        if mode == "redirect":
            redirected = output[process.stdout.fileno()]
            if b"TERMINAL-OUTPUT" not in redirected:
                raise AssertionError("Output bypassed redirected stdout")
        print(f"NativeAOT terminal test passed ({mode}).")
    finally:
        if process.poll() is None:
            process.kill()
        process.wait()
        process.stderr.close()
        if process.stdout is not None:
            process.stdout.close()
        os.close(master)


if __name__ == "__main__":
    for test_mode in ("terminal", "capture", "redirect"):
        run(os.path.abspath(sys.argv[1]), test_mode)
