// AuthService.cs — implements Register, Login, and JWT generation.
// IN: This is the most commonly discussed service in .NET interviews.
// Be ready to explain every decision:
// - Why UserManager over direct DbContext
// - Why SymmetricSecurityKey over asymmetric (RSA)
// - What claims go in the JWT and why
// - Why refresh tokens exist
// - Why we use ClockSkew = Zero
// - What happens if the JWT secret leaks

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Application.Features.Auth.Commands.Login;
using NexaStore.Application.Features.Auth.Commands.RefreshToken;
using NexaStore.Application.Features.Auth.Commands.Register;
using NexaStore.Identity.Models;
using NexaStore.Identity.Settings;

namespace NexaStore.Identity.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly JwtSettings _jwtSettings;

    // IN: Why UserManager and not DbContext directly?
    // UserManager encapsulates all Identity business logic:
    // - Password hashing (PBKDF2 by default — never store plain text)
    // - Email uniqueness enforcement
    // - Lockout tracking
    // - Role assignment
    // Going directly to DbContext bypasses all of this — a security risk.
    // UserManager is the correct abstraction for user operations.
    //
    // IN: Why IOptions<JwtSettings> over IConfiguration?
    // IOptions<T> gives you a strongly-typed, validated settings object.
    // IConfiguration["JwtSettings:Key"] is a magic string — typos are
    // silent null references at runtime. IOptions<T> fails fast at startup
    // if configuration is missing or malformed.
    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOptions<JwtSettings> jwtSettings)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtSettings = jwtSettings.Value;
    }

    // -------------------------------------------------------------------------
    // REGISTER
    // -------------------------------------------------------------------------
    public async Task<AuthResponseDto> RegisterAsync(RegisterCommand request,
        CancellationToken cancellationToken = default)
    {
        // Check if email is already registered
        // IN: UserManager.FindByEmailAsync hits AspNetUsers.
        // We check here for a clean error message. Identity would also reject
        // it in CreateAsync, but with a generic "DuplicateEmail" error code
        // that we'd have to parse. This gives a clearer exception message.
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser is not null)
            throw new InvalidOperationException(
                $"An account with email '{request.Email}' already exists.");

        // Build the ApplicationUser
        var user = new ApplicationUser
        {
            // IN: UserName = Email is the standard ASP.NET Core Identity
            // convention for email-based authentication. Identity uses UserName
            // internally for login — setting it to Email means users log in
            // with their email address, which is the universal UX expectation.
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            IsActive = true,

            // IN: EmailConfirmed = true skips the email verification step.
            // For a portfolio project this is fine. In production:
            // EmailConfirmed = false, send a verification email via IEmailService,
            // user clicks the link which calls ConfirmEmailAsync().
            // Login would then check EmailConfirmed before issuing a token.
            EmailConfirmed = true
        };

        // CreateAsync hashes the password (PBKDF2 + salt) and inserts the user.
        // IN: Never hash passwords yourself — always use UserManager.
        // PBKDF2 with 100,000 iterations + random salt is what Identity uses.
        // It is intentionally slow to resist brute-force attacks.
        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            // Collect all Identity errors into one readable message
            // e.g. "PasswordTooShort: Password must be at least 8 characters."
            var errors = string.Join(", ",
                result.Errors.Select(e => $"{e.Code}: {e.Description}"));
            throw new InvalidOperationException($"User registration failed: {errors}");
        }

        // Assign the Customer role — every registered user is a Customer by default.
        // IN: AddToRoleAsync inserts a row into AspNetUserRoles.
        // The role name must match exactly what was seeded in UserRoleConfiguration.
        // This is why we use the constant Roles.Customer — not a magic string.
        var roleResult = await _userManager.AddToRoleAsync(user, Roles.Customer);
        if (!roleResult.Succeeded)
        {
            // Role assignment failure should not leave a user without a role.
            // Roll back by deleting the user — atomic-ish cleanup.
            // IN: In production this would be wrapped in a transaction
            // using IDbContextTransaction to ensure user + role are atomic.
            await _userManager.DeleteAsync(user);
            throw new InvalidOperationException(
                "Failed to assign Customer role. Registration rolled back.");
        }

        // Generate and return tokens immediately after registration —
        // no need for a separate login step after sign-up (better UX)
        return await GenerateAuthResponseAsync(user);
    }

    // -------------------------------------------------------------------------
    // LOGIN
    // -------------------------------------------------------------------------
    public async Task<AuthResponseDto> LoginAsync(LoginCommand request,
        CancellationToken cancellationToken = default)
    {
        // Find user by email
        var user = await _userManager.FindByEmailAsync(request.Email)
            ?? throw new UnauthorizedAccessException("Invalid email or password.");

        // INTERVIEW: Why "Invalid email or password" and not "User not found"?
        // Revealing which part is wrong is a security vulnerability called
        // "username enumeration" — an attacker can use it to discover which
        // emails are registered. Always return the same message for both cases.

        // Check if account is active
        if (!user.IsActive)
            throw new UnauthorizedAccessException(
                "Your account has been disabled. Contact support.");

        // INTERVIEW: CheckPasswordSignInAsync vs PasswordSignInAsync.
        // PasswordSignInAsync creates a cookie session — wrong for a pure API.
        // CheckPasswordSignInAsync validates the password and handles lockout
        // tracking WITHOUT creating a session cookie. Correct for JWT APIs.
        // lockoutOnFailure: true — increments the failed access counter.
        // After 5 failures (configured in IdentityServiceRegistration),
        // the account is locked for 15 minutes automatically.
        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!signInResult.Succeeded)
        {
            if (signInResult.IsLockedOut)
                throw new UnauthorizedAccessException(
                    "Account locked due to multiple failed attempts. Try again in 15 minutes.");

            if (signInResult.IsNotAllowed)
                throw new UnauthorizedAccessException(
                    "Login not allowed. Verify your email address first.");

            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        return await GenerateAuthResponseAsync(user);
    }

    // -------------------------------------------------------------------------
    // REFRESH TOKEN
    // -------------------------------------------------------------------------
    // Full implementation in Week 3 Day 3 — stub for now
    // -------------------------------------------------------------------------
    // REFRESH TOKEN
    // -------------------------------------------------------------------------
    public async Task<AuthResponseDto> RefreshTokenAsync(
        RefreshTokenCommand request,
        CancellationToken cancellationToken = default)
    {
        // IN: The refresh flow has three jobs:
        // 1. Prove the caller owns a valid (but possibly expired) JWT
        // 2. Prove the caller holds the matching refresh token for that user
        // 3. Issue a new JWT + new refresh token, invalidate the old refresh token
        //
        // Why do we need BOTH the expired JWT and the refresh token?
        // The JWT tells us WHO the user is (via the sub claim) without a DB lookup.
        // The refresh token proves the caller is the legitimate owner of that identity.
        // Neither alone is sufficient:
        // - JWT alone: anyone who steals the expired JWT could get a new one forever
        // - Refresh token alone: attacker needs to know which user it belongs to
        // Together: attacker needs both — significantly harder to exploit.

        // --- Step 1: Extract claims from the EXPIRED JWT ---
        // IN: We validate the JWT structure and signature but NOT the expiry.
        // An expired token is still cryptographically valid — we just won't accept
        // it for API requests. Here we deliberately want to read an expired token.
        var principal = GetPrincipalFromExpiredToken(request.AccessToken);

        // Extract the user ID from the sub claim
        // IN: We read sub (not uid or email) because sub is the OIDC standard
        // claim for subject identity — guaranteed to be the user's unique ID.
        var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue("uid");

        if (string.IsNullOrEmpty(userId))
            throw new UnauthorizedAccessException(
                "Invalid access token — cannot extract user identity.");

        // --- Step 2: Load the user from DB ---
        // IN: This is the ONLY DB call in the refresh flow.
        // We use the ID from the JWT to find the user, then validate their
        // stored refresh token. This keeps the operation fast — one SELECT.
        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new UnauthorizedAccessException(
                "User not found. The account may have been deleted.");

        if (!user.IsActive)
            throw new UnauthorizedAccessException(
                "Account has been disabled.");

        // --- Step 3: Validate the refresh token ---
        // INTERVIEW: Three checks on the refresh token:
        // 1. It exists on the user (not null/empty)
        // 2. It matches the token the caller sent
        // 3. It has not expired
        //
        // INTERVIEW: Why string comparison and not a hash comparison?
        // In production you would store a hash of the refresh token and compare
        // hashes — this way even a DB breach doesn't expose valid refresh tokens.
        // For a portfolio project, plain comparison is acceptable and clear.
        if (string.IsNullOrEmpty(user.RefreshToken))
            throw new UnauthorizedAccessException(
                "No active refresh token found. Please log in again.");

        if (user.RefreshToken != request.RefreshToken)
            // INTERVIEW: Mismatched refresh token could mean:
            // a) The legitimate user already refreshed and the caller has a stale token
            // b) An attacker is replaying a stolen refresh token
            // Either way, invalidate everything — force re-login.
            // This is "refresh token rotation" — each refresh issues a new pair
            // and invalidates the old refresh token immediately.
            throw new UnauthorizedAccessException(
                "Invalid refresh token. Please log in again.");

        if (user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            // INTERVIEW: Refresh token expiry is separate from JWT expiry.
            // JWT: 60 minutes. Refresh token: 7 days.
            // After 7 days of inactivity the user must log in with a password again.
            // This is an intentional security boundary — "remember me" has a limit.
            throw new UnauthorizedAccessException(
                "Refresh token has expired. Please log in again.");

        // --- Step 4: Issue new token pair ---
        // INTERVIEW: Refresh token rotation — issue a NEW refresh token on every use.
        // The old refresh token is replaced in the DB immediately.
        // If an attacker steals the old refresh token and tries to use it:
        // - The legitimate user will have already rotated it
        // - The attacker's token no longer matches the DB value → 401
        // - The legitimate user's next refresh will also fail (DB was overwritten)
        //   → both are forced to re-login → the compromise is detected
        // This is called "refresh token family" invalidation in the OAuth2 spec.
        return await GenerateAuthResponseAsync(user);

        // GenerateAuthResponseAsync:
        // 1. Creates a new JWT with fresh claims (in case roles changed)
        // 2. Generates a new cryptographic refresh token
        // 3. Overwrites user.RefreshToken + user.RefreshTokenExpiryTime in DB
        // 4. Returns AuthResponseDto with both tokens
    }

    private ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
    {
        // IN: TokenValidationParameters here deliberately sets
        // ValidateLifetime = false — this is the ONLY place we do that.
        // We WANT to read an expired token. Every other token validation
        // in the system uses ValidateLifetime = true.
        //
        // We still validate:
        // - IssuerSigningKey: confirms the token was signed by US (not forged)
        // - Issuer + Audience: confirms it was issued for OUR API
        // We skip:
        // - Lifetime: expired tokens are valid input for the refresh flow
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_jwtSettings.Key)),

            ValidateIssuer = true,
            ValidIssuer = _jwtSettings.Issuer,

            ValidateAudience = true,
            ValidAudience = _jwtSettings.Audience,

            // IN: The critical flag — we accept expired tokens here.
            // Without this, ValidateToken throws on any expired token, making
            // the refresh flow impossible.
            ValidateLifetime = false,

            // Still enforce ClockSkew = Zero for consistency
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        ClaimsPrincipal principal;
        SecurityToken validatedToken;

        try
        {
            // ValidateToken parses the JWT, checks signature + issuer + audience,
            // and returns the ClaimsPrincipal (the identity embedded in the token)
            principal = tokenHandler.ValidateToken(
                token,
                tokenValidationParameters,
                out validatedToken);
        }
        catch (Exception ex)
        {
            // Catches: malformed tokens, invalid signatures, wrong issuer/audience
            // IN: Never expose the raw exception message to the client —
            // it can reveal implementation details useful to an attacker.
            throw new UnauthorizedAccessException(
                "Invalid access token.", ex);
        }

        // IN: Verify the token used the expected algorithm.
        // Algorithm confusion attacks: an attacker sends a token signed with
        // algorithm "none" or a weaker algorithm. Without this check, a lenient
        // library might accept it. Explicit algorithm check is a defence-in-depth measure.
        if (validatedToken is not JwtSecurityToken jwtToken ||
            !jwtToken.Header.Alg.Equals(
                SecurityAlgorithms.HmacSha256,
                StringComparison.InvariantCultureIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "Invalid token algorithm.");
        }

        return principal;
    }

    // -------------------------------------------------------------------------
    // PRIVATE HELPERS
    // -------------------------------------------------------------------------

    // Generates JWT + refresh token and persists the refresh token to the user.
    // Called by both Register and Login — single source of truth for token generation.
    // IN: Private helper keeps Register and Login DRY.
    // Both operations end with the same result — a valid token pair.
    private async Task<AuthResponseDto> GenerateAuthResponseAsync(
        ApplicationUser user)
    {
        var accessToken = await GenerateJwtTokenAsync(user);
        var refreshToken = GenerateRefreshToken();

        // Persist the refresh token to the user record
        // IN: Storing the refresh token on the user means only ONE
        // active refresh token per user (single device).
        // Multi-device support: separate RefreshToken table with
        // UserId FK, DeviceId, Token, ExpiryTime.
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(
            _jwtSettings.RefreshTokenDurationInDays);

        // Persist to DB via UserManager — goes through Identity pipeline
        await _userManager.UpdateAsync(user);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiry = DateTime.UtcNow.AddMinutes(_jwtSettings.DurationInMinutes)
        };
    }

    private async Task<string> GenerateJwtTokenAsync(ApplicationUser user)
    {
        // --- Build Claims ---
        // INTERVIEW: Claims are the payload of the JWT — key-value pairs
        // embedded in the token that describe the user.
        // The server reads these on every request WITHOUT a DB lookup.
        // This is what makes JWT stateless — no session store needed.
        var userRoles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            // sub (subject) — the user's unique ID.
            // INTERVIEW: JwtRegisteredClaimNames.Sub is the OIDC standard
            // claim for subject identity. We use it so the token is
            // standards-compliant. ICurrentUserService reads this claim.
            new(JwtRegisteredClaimNames.Sub,   user.Id),

            // jti (JWT ID) — unique identifier for this specific token.
            // INTERVIEW: jti enables token revocation — store jti values of
            // revoked tokens in a blocklist. On each request, check if the
            // token's jti is in the blocklist. Without jti, you cannot
            // revoke a specific token before it expires.
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),

            // Standard email claim
            new(JwtRegisteredClaimNames.Email, user.Email!),

            // Custom claims — not in the JWT standard but useful for our API
            new("uid",        user.Id),
            new("firstName",  user.FirstName),
            new("lastName",   user.LastName),
        };

        // Add one claim per role — user could theoretically have multiple
        // INTERVIEW: Role claims in the JWT mean [Authorize(Roles = "Admin")]
        // works without a DB lookup on every request.
        // The role is read directly from the token's claims — stateless.
        foreach (var role in userRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // --- Build Token ---
        // INTERVIEW: SymmetricSecurityKey uses the same key to sign and verify.
        // Simple and fast — correct for single-server or same-organisation APIs.
        // Asymmetric (RSA) would be needed if EXTERNAL services need to verify
        // our tokens without sharing the secret — e.g. microservices with
        // a dedicated auth server issuing tokens verified by multiple APIs.
        var symmetricKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtSettings.Key));

        var signingCredentials = new SigningCredentials(
            symmetricKey,
            // INTERVIEW: HmacSha256 is the industry standard for JWT signing.
            // HmacSha512 is more secure but overkill for most APIs.
            // Never use "none" algorithm — unsigned tokens are trivially forged.
            SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.DurationInMinutes),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = signingCredentials,

            // INTERVIEW: IssuedAt and NotBefore are set automatically
            // by JwtSecurityTokenHandler when not explicitly provided.
            // Explicit here for clarity.
            IssuedAt = DateTime.UtcNow,
            NotBefore = DateTime.UtcNow
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        // Serialize the token to its compact string form:
        // base64(header).base64(payload).base64(signature)
        return tokenHandler.WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        // INTERVIEW: Why not use Guid.NewGuid() for the refresh token?
        // Guid is 128 bits of pseudo-random data — predictable enough to be
        // a security concern for a secret token.
        // RandomNumberGenerator.GetBytes() uses the OS CSPRNG
        // (Cryptographically Secure Pseudo-Random Number Generator) —
        // the same entropy source used for TLS key generation.
        // 64 bytes = 512 bits of cryptographic randomness.
        // Base64 encoded = 88-character string, URL-safe if needed.
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
