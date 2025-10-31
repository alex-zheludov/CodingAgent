namespace MicroMind.Core.Exceptions;

public class MicroMindException : Exception
{
    public MicroMindException()
    {
    }

    public MicroMindException(string message) : base(message)
    {
    }

    public MicroMindException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
