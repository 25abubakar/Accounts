namespace Accounts.Services;

public sealed class ChatForbiddenException(string message) : Exception(message);
public sealed class ChatNotFoundException(string message) : Exception(message);
public sealed class ChatConflictException(string message) : Exception(message);
public sealed class ChatValidationException(string message) : Exception(message);

internal static class ChatExceptionHelper
{
    public static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException
        || (exception is Microsoft.Data.SqlClient.SqlException sql
            && sql.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase));
}
