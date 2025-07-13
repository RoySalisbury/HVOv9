using System.ComponentModel;
using HVO.ComponentModel;

namespace HVO;

/// <summary>
/// Example usage patterns for the Result&lt;T&gt; and Result&lt;T, TEnum&gt; types
/// </summary>
public static class ResultUsageExamples
{
    /// <summary>
    /// Example error codes for demonstration
    /// </summary>
    public enum DatabaseError
    {
        [Description("Connection to database failed")]
        ConnectionFailed,
        
        [Description("Record not found in database")]
        RecordNotFound,
        
        [Description("Database timeout occurred")]
        Timeout,
        
        [Description("Access denied to database resource")]
        AccessDenied
    }

    /// <summary>
    /// Example of basic Result&lt;T&gt; usage with exception handling
    /// </summary>
    public static Result<string> ReadFileContent(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            return Result<string>.Success(content);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(ex);
        }
    }

    /// <summary>
    /// Example of Result&lt;T, TEnum&gt; usage with typed error codes
    /// </summary>
    public static Result<User, DatabaseError> GetUserById(int userId)
    {
        if (userId <= 0)
            return DatabaseError.AccessDenied;

        // Simulate database lookup
        if (userId == 404)
            return DatabaseError.RecordNotFound;

        if (userId == 500)
            return (DatabaseError.ConnectionFailed, "Database server is unreachable");

        return new User { Id = userId, Name = $"User_{userId}" };
    }

    /// <summary>
    /// Example of using Match for functional-style error handling
    /// </summary>
    public static string ProcessUser(int userId)
    {
        var result = GetUserById(userId);
        
        return result.Match(
            success: user => $"Found user: {user.Name}",
            failure: error => $"Error {error.Code}: {error.Message}"
        );
    }

    /// <summary>
    /// Example of chaining operations with Result pattern
    /// </summary>
    public static Result<string> ProcessFileAndUser(string filePath, int userId)
    {
        var fileResult = ReadFileContent(filePath);
        if (!fileResult.IsSuccessful)
            return fileResult.Error!;

        var userResult = GetUserById(userId);
        if (!userResult.IsSuccessful)
            return new InvalidOperationException($"User error: {userResult.Error.Message}");

        return $"File: {fileResult.Value.Length} chars, User: {userResult.Value.Name}";
    }

    /// <summary>
    /// Simple user class for demonstration
    /// </summary>
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
