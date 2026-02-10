using System;

namespace Inventory.Infrastructure.Services
{
    public class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }

    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message) { }
        public ConflictException(string message, Exception innerException) : base(message, innerException) { }
    }
}
