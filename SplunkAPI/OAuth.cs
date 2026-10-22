using Microsoft.EntityFrameworkCore;
namespace SplunkAPI;

public class OAuth(DbContextOptions<OAuth> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
    }
}