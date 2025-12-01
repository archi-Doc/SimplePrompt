namespace DelayTest;

internal class Program
{
    public static async Task Main(string[] args)
    {
        /*while (true)
        {
            Console.Write("d");
            await Task.Delay(100).ConfigureAwait(false);
            Console.Write("e");
        }*/

        Console.Write("> ");
        var st = Console.ReadLine();
        Console.Write(st);
    }
}
