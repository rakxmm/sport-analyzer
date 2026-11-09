using FootballApp.Application.Common.Interfaces;

namespace FootballApp.Application.Tests.Fakes;

public class FakeCurrentUser(string? userId) : ICurrentUserService
{
    public string? UserId => userId;
    public bool IsAuthenticated => userId is not null;
}
