using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;
using Arc.Collections;
using Arc.Threading;

namespace SimplePrompt;

public partial class SimpleConsole
{
    private class Worker : TaskCore
    {
        private const int QueueCapacity = 256;
        private const int DelayInMilliseconds = 10;

        private readonly SimpleConsole simpleConsole;
        private readonly CircularQueue<string?> queue = new(QueueCapacity);

        public Worker(SimpleConsole simpleConsole)
            : base(default, Process, true)
        {
            this.simpleConsole = simpleConsole;
        }

        public void Add(string message)
        {
            this.queue.TryEnqueue(message);
        }

        private static async Task Process(object? obj)
        {
            var worker = (Worker)obj!;
            while (await worker.Delay(1000).ConfigureAwait(false))
            {
                while (worker.queue.TryDequeue(out var message))
                {
                    worker.ProcessMessage(message);
                }
            }
        }

        private void ProcessMessage(string message)
        {
            if (!this.simpleConsole.IsReadLineInProgress)
            {
                this.simpleConsole.CheckCursor();

                this.simpleConsole.WriteInternal(message, false);

                this.simpleConsole.CheckCursor();
            }
        }
    }
}
