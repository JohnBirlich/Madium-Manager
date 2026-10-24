using MadiumManager.Domain.Accounts;
using MadiumManager.Infrastructure.Accounts;
using Xunit;

namespace MadiumManager.Tests;

public sealed class JsonAccountRepositoryTests
{
    [Fact]
    public async Task SaveAndLoad_PreservesNonSecretAccountMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MadiumManagerTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "accounts.json");
        try
        {
            var repository = new JsonAccountRepository(path);
            var expected = new AccountProfile(
                "account-id",
                "North · Demo",
                "demo.north",
                "No credentials in this record",
                DateTimeOffset.UtcNow);

            await repository.SaveAsync([expected], CancellationToken.None);
            var actual = await repository.LoadAsync(CancellationToken.None);

            var account = Assert.Single(actual);
            Assert.Equal(expected, account);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_ReturnsEmptyCollection_WhenRepositoryDoesNotExist()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "MadiumManagerTests",
            Guid.NewGuid().ToString("N"),
            "accounts.json");
        var repository = new JsonAccountRepository(path);

        var accounts = await repository.LoadAsync(CancellationToken.None);

        Assert.Empty(accounts);
    }
}
