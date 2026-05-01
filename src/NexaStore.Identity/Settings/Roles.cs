// Roles.cs — role name constants used across the entire codebase.
// INTERVIEW: Centralising role names as constants is a small but important
// discipline. [Authorize(Roles = "Admin")] scattered everywhere is a
// maintenance nightmare — rename the role and you have 20 files to update.
// [Authorize(Roles = Roles.Admin)] means one change propagates everywhere.

namespace NexaStore.Identity.Settings;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}
