namespace MicroMind.Core.Exceptions;

public class ModelDownloadException : MicroMindException
{
    public ModelDownloadException()
    {
    }

    public ModelDownloadException(string message) : base(message)
    {
    }

    public ModelDownloadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
