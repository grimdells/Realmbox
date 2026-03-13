namespace Realmbox.Core.Exceptions
{
    public class ExaltExeNotFoundException : Exception
    {
        public ExaltExeNotFoundException() { }

        public ExaltExeNotFoundException(string? message) : base(message) { }

        public ExaltExeNotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
