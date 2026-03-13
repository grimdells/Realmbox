namespace Realmbox.Core.Exceptions
{
    public class AccessTokenParseFailedException : Exception
    {
        public AccessTokenParseFailedException() { }

        public AccessTokenParseFailedException(string? message) : base(message) { }

        public AccessTokenParseFailedException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
