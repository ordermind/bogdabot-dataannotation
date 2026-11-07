using Bogdabot.Server.Shared.Config.Database;
using Microsoft.EntityFrameworkCore;

namespace Bogdabot.Server.Infrastructure.Miscellaneous.Database;

public interface IDbOptionsFactory
{
    DbContextOptionsBuilder SetDatabaseOptions(
        DbContextOptionsBuilder optionsBuilder,
        IDatabaseConfig dbConfig);
}