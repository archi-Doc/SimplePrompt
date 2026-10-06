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


def run(executable, mode, timeout=30):
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
    utf8_input = "日本語😀abc".encode()
    # Split UTF-8 scalars and terminal sequences across multiple native reads.
    input_chunks = [utf8_input[:1], utf8_input[1:5], utf8_input[5:],
                    b"\x1b", b"[", b"D", b"\x1b[", b"3", b"~Z\r"]
    input_index = 0
    next_input_time = None
    deadline = time.monotonic() + timeout
    try:
        while streams and time.monotonic() < deadline:
            wait_time = 0.1 if next_input_time is None else min(0.1, max(0, next_input_time - time.monotonic()))
            readable, _, _ = select.select(streams, [], [], wait_time)
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
                    sent_input = True
                    next_input_time = time.monotonic()
            if next_input_time is not None and time.monotonic() >= next_input_time:
                # Keep servicing terminal queries while each fragment awaits its turn.
                os.write(master, input_chunks[input_index])
                input_index += 1
                next_input_time = time.monotonic() + 0.02 if input_index < len(input_chunks) else None
            if process.poll() is not None and not readable:
                break

        combined = b"".join(output.values()).decode(errors="replace")
        try:
            # EOF can arrive before the child's exit status becomes available.
            # Wait for shutdown using the remaining time, including after EOF.
            process.wait(timeout=max(0, deadline - time.monotonic()))
        except subprocess.TimeoutExpired as error:
            raise TimeoutError(
                f"{mode}: terminal test timed out after {timeout}s "
                f"(input sent: {sent_input})\n{combined}"
            ) from error
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
