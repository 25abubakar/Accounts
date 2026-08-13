namespace Accounts.Services;

public sealed class ChatForbiddenException(string message) : Exception(message);
public sealed class ChatNotFoundException(string message) : Exception(message);
public sealed class ChatConflictException(string message) : Exception(message);
public sealed class ChatValidationException(string message) : Exception(message);
