using System.CommandLine;

namespace CodeVectorization.Cli.Commands;

public static class SearchCommand
{
    public static Command Create()
    {
        var queryArgument = new Argument<string>("query", "Semantic search query");
        var repoOption = new Option<string?>("--repo", "Filter search to a specific repository");

        var command = new Command("search", "Search indexed repositories")
        {
            queryArgument,
            repoOption
        };

        command.SetHandler(async (string query, string? repo) =>
        {
            Console.WriteLine($"Searching for: {query}");
            if (repo != null)
                Console.WriteLine($"Filtered to repository: {repo}");
            // Implementation will be added in later tickets
            await Task.CompletedTask;
        }, queryArgument, repoOption);

        return command;
    }
}
