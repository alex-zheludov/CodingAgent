namespace MicroMind.Core.Exceptions;

public class ModelLoadException : MicroMindException
{
    public ModelLoadException()
    {
    }

    public ModelLoadException(string message) : base(message)
    {
    }

    public ModelLoadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
