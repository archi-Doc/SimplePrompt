namespace DelayTest;

internal class Program
{
    public static async Task Main(string[] args)
    {
        await Log();
    }

    private static async Task Log()
    {
        Console.WriteLine("Log test");
        while (true)
        {
            await Task.Delay(1_000).ConfigureAwait(false);

            var st = $"Cursor {Console.CursorLeft}, {Console.CursorTop} Window {Console.WindowWidth}, {Console.WindowHeight}\n";
            File.AppendAllText("log.txt", st);
        }
    }

    private static async Task TaskDelay()
    {
        while (true)
        {
            Console.Write("d");
            await Task.Delay(100).ConfigureAwait(false);
            Console.Write("e");
        }
    }


    private static async Task ReadLine()
    {
        Console.Write("> ");
        var st = Console.ReadLine();
        Console.Write(st);
    }
}
