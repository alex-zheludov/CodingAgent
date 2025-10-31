namespace MicroMind.Core.Exceptions;

public class ModelValidationException : MicroMindException
{
    public ModelValidationException()
    {
    }

    public ModelValidationException(string message) : base(message)
    {
    }

    public ModelValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
