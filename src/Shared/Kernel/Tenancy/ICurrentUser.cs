namespace Kernel.Tenancy;

/// <summary>The signed-in user of the current request.</summary>
public interface ICurrentUser
{
    /// <summary>True when the request carries an authenticated principal.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The user's id. Throws when not authenticated.</summary>
    Guid UserId { get; }

    /// <summary>The user's email. Throws when not authenticated.</summary>
    string Email { get; }

    /// <summary>"Admin" or "Staff". Throws when not authenticated.</summary>
    string Role { get; }
}