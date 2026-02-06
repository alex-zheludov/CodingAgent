using System.CommandLine;

namespace CodeVectorization.Cli.Commands;

public static class IndexCommand
{
    public static Command Create()
    {
        var pathArgument = new Argument<string>("path", "Path to the repository to index");

        var command = new Command("index", "Index a repository for semantic search")
        {
            pathArgument
        };

        command.SetHandler(async (string path) =>
        {
            Console.WriteLine($"Indexing repository at: {path}");
            // Implementation will be added in later tickets
            await Task.CompletedTask;
        }, pathArgument);

        return command;
    }
}
