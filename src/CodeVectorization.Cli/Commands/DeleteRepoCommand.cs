using System.CommandLine;

namespace CodeVectorization.Cli.Commands;

public static class DeleteRepoCommand
{
    public static Command Create()
    {
        var nameArgument = new Argument<string>("name", "Name of the repository to delete");

        var command = new Command("delete-repo", "Delete an indexed repository")
        {
            nameArgument
        };

        command.SetHandler(async (string name) =>
        {
            Console.WriteLine($"Deleting repository: {name}");
            // Implementation will be added in later tickets
            await Task.CompletedTask;
        }, nameArgument);

        return command;
    }
}
