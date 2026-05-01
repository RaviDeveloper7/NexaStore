// src/NexaStore.Identity/DbContext/NexaStoreIdentityDbContextFactory.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NexaStore.Identity.DbContext;

public class NexaStoreIdentityDbContextFactory
    : IDesignTimeDbContextFactory<NexaStoreIdentityDbContext>
{
    public NexaStoreIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder =
            new DbContextOptionsBuilder<NexaStoreIdentityDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=NexaStoreDb;Trusted_Connection=True;TrustServerCertificate=True;");

        return new NexaStoreIdentityDbContext(optionsBuilder.Options);
    }
}
