namespace FootballApp.Application.Common.Exceptions;

/// <summary>Thrown when a request targets a match/clip the current Clerk user does not own (FR-5).</summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException() : base("You do not have access to this resource.")
    {
    }
}
