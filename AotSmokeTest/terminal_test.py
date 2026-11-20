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

# RawConsole.EscapeSequenceTimeoutMilliseconds: the fragments of an escape sequence must arrive within it,
# or the sequence is decoded as separate keys, which is the intended behavior for slow input.
ESCAPE_TIMEOUT = 0.1
INPUT_INTERVAL = 0.02
ATTEMPTS = 3

UTF8_INPUT = "日本語😀abc".encode()
# Split UTF-8 scalars and terminal sequences across multiple native reads.
INPUT_CHUNKS = [UTF8_INPUT[:1], UTF8_INPUT[1:5], UTF8_INPUT[5:],
                b"\x1b", b"[", b"D", b"\x1b[", b"3", b"~Z\r"]
# The first and last chunk of each escape sequence (Left arrow and Delete).
ESCAPE_CHUNKS = [(3, 5), (6, 8)]


class InputTimingError(AssertionError):
    """The fragments of an escape sequence were sent further apart than the escape timeout."""


def describe_input_timing(sent_times):
    if not sent_times:
        return ""
    offsets = ", ".join(f"{(sent - sent_times[0]) * 1000:.0f}" for sent in sent_times)
    return f"\ninput chunks sent at +ms: {offsets}"


def escape_timing_exceeded(sent_times):
    return any(last < len(sent_times) and sent_times[last] - sent_times[first] >= ESCAPE_TIMEOUT
               for first, last in ESCAPE_CHUNKS)


def run(executable, mode, timeout=30, input_interval=INPUT_INTERVAL):
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
    sent_times = []
    input_index = 0
    next_input_time = None
    deadline = time.monotonic() + timeout
    try:
        while streams and time.monotonic() < deadline:
            # Poll without sleeping while input remains: a sleep can overshoot on a loaded runner
            # (macOS coalesces timers), spreading an escape sequence beyond the escape timeout.
            wait_time = 0.1 if next_input_time is None else 0
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
                os.write(master, INPUT_CHUNKS[input_index])
                sent_times.append(time.monotonic())
                input_index += 1
                next_input_time = next_input_time + input_interval if input_index < len(INPUT_CHUNKS) else None
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
                f"(input sent: {sent_input})\n{combined}{describe_input_timing(sent_times)}"
            ) from error
        if process.returncode != 0 or "NativeAOT smoke test passed." not in combined:
            message = f"{mode}: exit {process.returncode}\n{combined}{describe_input_timing(sent_times)}"
            if escape_timing_exceeded(sent_times):
                raise InputTimingError(message)
            raise AssertionError(message)
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


def run_with_retries(executable, mode, attempts=ATTEMPTS, **options):
    """Repeat a run that failed only after the runner delayed an escape sequence beyond the escape timeout."""
    for attempt in range(1, attempts + 1):
        try:
            run(executable, mode, **options)
            return
        except InputTimingError as error:
            if attempt == attempts:
                raise
            print(f"{mode}: attempt {attempt} of {attempts} sent an escape sequence slower than "
                  f"the escape timeout; retrying.\n{error}")


if __name__ == "__main__":
    for test_mode in ("terminal", "capture", "redirect"):
        run_with_retries(os.path.abspath(sys.argv[1]), test_mode)
