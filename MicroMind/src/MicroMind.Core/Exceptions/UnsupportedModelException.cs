namespace MicroMind.Core.Exceptions;

public class UnsupportedModelException : MicroMindException
{
    public UnsupportedModelException()
    {
    }

    public UnsupportedModelException(string message) : base(message)
    {
    }

    public UnsupportedModelException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
