using System.CommandLine;

namespace CodeVectorization.Cli.Commands;

public static class ListReposCommand
{
    public static Command Create()
    {
        var command = new Command("list-repos", "List all indexed repositories");

        command.SetHandler(async () =>
        {
            Console.WriteLine("Listing indexed repositories...");
            // Implementation will be added in later tickets
            await Task.CompletedTask;
        });

        return command;
    }
}
