namespace Realmbox.Core.Exceptions
{
    public class ExaltPathNotFoundException : Exception
    {
        public ExaltPathNotFoundException() { }

        public ExaltPathNotFoundException(string? message) : base(message) { }

        public ExaltPathNotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
