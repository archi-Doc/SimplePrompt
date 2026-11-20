"""Regression tests for pseudo-terminal process completion and timeouts."""

import contextlib
import io
from pathlib import Path
import tempfile
import time
import unittest

from terminal_test import ESCAPE_TIMEOUT, InputTimingError, run, run_with_retries


class TerminalTest(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.executable = Path(self.directory.name) / "terminal-child"

    def write_child(self, body):
        self.executable.write_text(
            "#!/usr/bin/env python3\nimport os\nimport time\n" + body,
            encoding="utf-8",
        )
        self.executable.chmod(0o755)

    def run_child(self, mode="terminal", timeout=5, **options):
        with contextlib.redirect_stdout(io.StringIO()):
            run(str(self.executable), mode, timeout=timeout, **options)

    def write_failing_child_after_input(self, counter):
        # Signals readiness so that the input is sent, then fails after receiving all of it, even at slow intervals.
        self.write_child(
            f'with open({str(counter)!r}, "a") as file:\n'
            '    file.write("x")\n'
            'os.write(2, b"READY\\n")\n'
            "time.sleep(1.2)\n"
            "os._exit(1)\n"
        )

    def test_failure_reports_input_timing(self):
        self.write_failing_child_after_input(Path(self.directory.name) / "runs")
        with self.assertRaisesRegex(AssertionError, "terminal: exit 1\\n(.|\\n)*input chunks sent at \\+ms: 0, "):
            self.run_child()

    def test_slow_escape_input_is_a_timing_error(self):
        self.write_failing_child_after_input(Path(self.directory.name) / "runs")
        with self.assertRaises(InputTimingError):
            self.run_child(input_interval=ESCAPE_TIMEOUT)

    def test_retries_only_timing_errors(self):
        counter = Path(self.directory.name) / "runs"
        self.write_failing_child_after_input(counter)
        with contextlib.redirect_stdout(io.StringIO()) as printed:
            with self.assertRaises(InputTimingError):
                run_with_retries(str(self.executable), "terminal", attempts=2, timeout=5, input_interval=ESCAPE_TIMEOUT)
        self.assertEqual("xx", counter.read_text(encoding="utf-8"))
        self.assertIn("attempt 1 of 2", printed.getvalue())

        counter.unlink()
        self.write_child(f'open({str(counter)!r}, "a").write("x")\nos.closerange(0, 3)\nos._exit(3)\n')
        with contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaisesRegex(AssertionError, "terminal: exit 3") as error:
                run_with_retries(str(self.executable), "terminal", attempts=2, timeout=5)
        self.assertNotIsInstance(error.exception, InputTimingError)
        self.assertEqual("x", counter.read_text(encoding="utf-8"))

    def test_waits_for_exit_after_all_streams_close(self):
        self.write_child(
            'os.write(1, b"TERMINAL-OUTPUT\\nNativeAOT smoke test passed.\\n")\n'
            "os.closerange(0, 3)\n"
            "time.sleep(0.2)\n"
        )
        for mode in ("terminal", "capture", "redirect"):
            with self.subTest(mode=mode):
                self.run_child(mode)

    def test_reports_nonzero_exit_after_streams_close(self):
        self.write_child(
            'os.write(2, b"shutdown failed\\n")\n'
            "os.closerange(0, 3)\n"
            "time.sleep(0.2)\n"
            "os._exit(7)\n"
        )
        with self.assertRaisesRegex(AssertionError, "terminal: exit 7\\nshutdown failed"):
            self.run_child()

    def test_requires_success_message(self):
        self.write_child("os.closerange(0, 3)\n")
        with self.assertRaisesRegex(AssertionError, "terminal: exit 0"):
            self.run_child()

    def test_preserves_timeout_and_output_with_open_or_closed_streams(self):
        for close_streams in (False, True):
            with self.subTest(close_streams=close_streams):
                self.write_child(
                    'os.write(2, b"still shutting down\\n")\n'
                    + ("os.closerange(0, 3)\n" if close_streams else "")
                    + "time.sleep(60)\n"
                )
                started = time.monotonic()
                with self.assertRaises(TimeoutError) as error:
                    self.run_child(timeout=1)
                self.assertIn("still shutting down", str(error.exception))
                self.assertIn("input sent: False", str(error.exception))
                self.assertGreaterEqual(time.monotonic() - started, 1)
                self.assertLess(time.monotonic() - started, 5)


if __name__ == "__main__":
    unittest.main()
