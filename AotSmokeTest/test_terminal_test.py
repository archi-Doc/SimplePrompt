"""Regression tests for pseudo-terminal process completion and timeouts."""

import contextlib
import io
from pathlib import Path
import tempfile
import time
import unittest

from terminal_test import run


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

    def run_child(self, mode="terminal", timeout=5):
        with contextlib.redirect_stdout(io.StringIO()):
            run(str(self.executable), mode, timeout=timeout)

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
