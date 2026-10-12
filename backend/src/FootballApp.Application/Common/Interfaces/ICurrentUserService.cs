namespace FootballApp.Application.Common.Interfaces;

/// <summary>Wraps the Clerk user ID resolved from the validated JWT's "sub" claim on the current request.</summary>
public interface ICurrentUserService
{
    string? UserId { get; }
    bool IsAuthenticated { get; }
}
