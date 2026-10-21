using System.Globalization;
using MatPlatform;
using MatTrakr.Services;

namespace MatTrakr.Platform;

/// <summary>
/// MatTrakr's side of the sign-in through MatIdentity (MatPlatform <see cref="IMatIdentityUsers"/>): the person is mapped to a
/// local user by <see cref="UserService.SignInExternalAsync"/> (by <c>sub</c>, else a new user; the claim <c>role</c> when it is there)
/// and then signed in with MatTrakr's own DB-backed session, like a local sign-in, keeping the ID token with the session for the
/// sign-out at MatIdentity. An unlinked user named like the e-mail address is not entered: MatPlatform keeps a pending link and the
/// local sign-in links it after its password (<see cref="Pages.Account.LoginModel"/>).
/// The back-channel logout (<see cref="IMatIdentitySessions"/>) deletes the session rows (<see cref="AuthService.EndIdentitySessionsAsync"/>).
/// </summary>
public sealed class TrakrIdentityUsers(UserService users, AuthService auth, ILogger<TrakrIdentityUsers> log) : IMatIdentityUsers, IMatIdentitySessions
{
    public async Task<MatIdentitySignInResult> SignInAsync(MatIdentityPerson person, HttpContext http, CancellationToken ct)
    {
        var identity = new ExternalIdentity(person.Subject, person.Email, person.Name, person.PreferredUserName, person.Role);

        var result = await users.SignInExternalAsync(identity, ct);
        if (result.PendingLink is { } match)
        {
            log.LogInformation("User {UserId} matches a MatIdentity person by name; it is linked after its password only", match.Id);
            return MatIdentitySignInResult.LinkRequired(match.Id.ToString(CultureInfo.InvariantCulture));
        }

        if (result.User is not { } user)
        {
            return MatIdentitySignInResult.Refused(result.Error ?? "refused");
        }

        await auth.SignInAsync(http, user, persistent: true, person.IdToken, person.SessionId);
        log.LogInformation("User {UserId} ({Username}) signed in through MatIdentity", user.Id, user.Username);

        return MatIdentitySignInResult.Success();
    }

    public async Task EndSessionsAsync(string? subject, string? sessionId, CancellationToken ct)
    {
        var ended = await auth.EndIdentitySessionsAsync(subject, sessionId, ct);
        log.LogInformation("MatIdentity signed out {Subject} (sid {SessionId}): {Count} session(s) ended", subject, sessionId, ended);
    }
}
