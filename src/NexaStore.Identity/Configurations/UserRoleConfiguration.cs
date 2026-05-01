// UserRoleConfiguration.cs — seeds the Admin and Customer roles.
// INTERVIEW: Why seed roles via EF HasData instead of a startup script?
// HasData() is idempotent and versioned — it lives in a migration.
// A startup script runs every time the app boots, needs its own idempotency
// logic, and isn't tracked by source control as a schema change.
// HasData() runs exactly once when the migration is applied — clean and auditable.
//
// INTERVIEW: We seed ROLES only, not users.
// Seeding a hardcoded Admin user with a hardcoded password is a security risk —
// that password ends up in your migration history in source control forever.
// The correct approach: seed roles here, create the first admin user via
// a one-time setup endpoint or a secure out-of-band process.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NexaStore.Identity.Configurations;

public class UserRoleConfiguration : IEntityTypeConfiguration<IdentityRole>
{
    // INTERVIEW: Role IDs must be hardcoded stable Guids — same reason as entity seed data.
    // These IDs are referenced in AspNetUserRoles when assigning roles to users.
    // If they change between migrations, existing user-role assignments break.
    public const string AdminRoleId = "A0000000-0000-0000-0000-000000000001";
    public const string CustomerRoleId = "A0000000-0000-0000-0000-000000000002";

    // Role name constants — used throughout the codebase for [Authorize(Roles = "Admin")]
    // and ICurrentUserService.IsAdmin checks.
    // INTERVIEW: Constants over magic strings — a typo in "Admin" is a runtime bug.
    // A typo in Roles.Admin is a compile-time error.
    public const string AdminRole = "Admin";
    public const string CustomerRole = "Customer";

    public void Configure(EntityTypeBuilder<IdentityRole> builder)
    {
        builder.HasData(
            new IdentityRole
            {
                Id = AdminRoleId,
                Name = AdminRole,
                // INTERVIEW: NormalizedName is required by Identity for case-insensitive
                // role lookups. Identity always normalizes to UPPERCASE internally.
                // Forgetting this means role assignment silently fails.
                NormalizedName = AdminRole.ToUpperInvariant(),
                ConcurrencyStamp = AdminRoleId   // Stable value — prevents unnecessary migrations
            },
            new IdentityRole
            {
                Id = CustomerRoleId,
                Name = CustomerRole,
                NormalizedName = CustomerRole.ToUpperInvariant(),
                ConcurrencyStamp = CustomerRoleId
            }
        );
    }
}
