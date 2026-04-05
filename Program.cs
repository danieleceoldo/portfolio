class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0].ToLower() == "help")
        {
            Console.WriteLine("Usage: dotnet run <PortfolioName> <Command> [<Args>]");
            Console.WriteLine("Commands:");
            Console.WriteLine("  add <MorningstarCode> - Adds a fund to the portfolio using its Morningstar code.");
            Console.WriteLine("  update - Updates the NAV quotes for all funds in the portfolio.");
            Console.WriteLine("  list - Lists all funds in the portfolio along with their NAV quotes.");
            Console.WriteLine("  help - Displays this help message.");
            return;
        }
        if (args.Length < 2)
        {
            Console.WriteLine("Wrong number of arguments. Use 'help' for more information.");
            return;
        }
        var portfolio = new Portfolio(args[0]);
        if (args[1].ToLower() == "add" && args.Length == 3)
        {
            await portfolio.Add(args[2]);
        }
        else if (args[1].ToLower() == "update")
        {
            await portfolio.Update();
        }
        else if (args[1].ToLower() == "list")
        {
            portfolio.List();
        }
        else
        {
            Console.WriteLine("Unknown or wrong command syntax. Use 'help' for more information.");
        }
    }
}