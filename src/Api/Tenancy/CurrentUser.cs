using Kernel.Tenancy;

namespace Api.Tenancy;

/// <summary>The request's signed-in user, set once by <see cref="TenantMiddleware"/>.</summary>
internal sealed class CurrentUser : ICurrentUser
{
    private (Guid UserId, string Email, string Role)? _user;

    public bool IsAuthenticated => _user.HasValue;

    public Guid UserId => User.UserId;

    public string Email => User.Email;

    public string Role => User.Role;

    private (Guid UserId, string Email, string Role) User => _user ?? throw new InvalidOperationException("No user is signed in.");

    internal void Set(Guid userId, string email, string role)
    {
        if (_user.HasValue)
        {
            throw new InvalidOperationException("This scope already has a user.");
        }

        _user = (userId, email, role);
    }
}