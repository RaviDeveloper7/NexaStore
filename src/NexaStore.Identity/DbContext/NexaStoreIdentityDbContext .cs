// IdentityDbContext.cs — separate DbContext exclusively for Identity tables.
// INTERVIEW: Why two DbContexts (AppDbContext + IdentityDbContext)?
//
// Option A — One DbContext that inherits IdentityDbContext<ApplicationUser>:
//   Simpler but couples your domain schema to the Identity schema.
//   One migration file contains both business tables and Identity tables.
//   Swapping Identity providers (e.g. moving to Keycloak) means migrating
//   your entire schema, not just the Identity portion.
//
// Option B — Two separate DbContexts (our approach):
//   AppDbContext owns all business tables (Orders, Products, etc.)
//   IdentityDbContext owns all Identity tables (AspNetUsers, AspNetRoles, etc.)
//   Migrations are independent — Identity schema never touches business schema.
//   INTERVIEW: The trade-off is you can't do a JOIN between Identity tables
//   and business tables in a single EF query. We use CustomerId (Guid parsed
//   from the string IdentityUser.Id) as the link — resolved in the handler,
//   not in a JOIN.

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NexaStore.Identity.Configurations;
using NexaStore.Identity.Models;

namespace NexaStore.Identity.DbContext;

public class NexaStoreIdentityDbContext
    : IdentityDbContext<ApplicationUser>
{
    // INTERVIEW: IdentityDbContext<ApplicationUser> automatically creates
    // these tables via migration:
    // AspNetUsers       — ApplicationUser rows
    // AspNetRoles       — IdentityRole rows
    // AspNetUserRoles   — many-to-many join
    // AspNetUserClaims  — per-user claims
    // AspNetRoleClaims  — per-role claims
    // AspNetUserLogins  — external OAuth logins
    // AspNetUserTokens  — 2FA tokens etc.
    // All of this comes for free — you don't write a single line of schema.

    public NexaStoreIdentityDbContext(
        DbContextOptions<NexaStoreIdentityDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // MUST call base — this sets up all the Identity table relationships.
        // Forgetting base.OnModelCreating() is a common mistake that causes
        // missing FK constraints and broken Identity queries.
        base.OnModelCreating(builder);

        // Apply role seeding configuration
        // INTERVIEW: ApplyConfiguration keeps the seeding logic out of the DbContext.
        // Same pattern as AppDbContext — one config class per concern.
        builder.ApplyConfiguration(new UserRoleConfiguration());

        // --- Customise Identity table columns ---

        // Add FirstName, LastName, RefreshToken columns to AspNetUsers table.
        // INTERVIEW: Because ApplicationUser extends IdentityUser, EF automatically
        // adds our custom columns to the AspNetUsers table.
        // No extra configuration needed — EF discovers them via the model.

        // Enforce max lengths on custom fields for storage efficiency
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FirstName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(u => u.LastName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(u => u.RefreshToken)
                .HasMaxLength(500)
                .IsRequired(false);

            entity.Property(u => u.RefreshTokenExpiryTime)
                .IsRequired(false);

            entity.Property(u => u.IsActive)
                .IsRequired()
                .HasDefaultValue(true);
        });
    }
}
