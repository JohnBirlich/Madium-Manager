using System.Net;
using MadiumManager.Domain.Servers;
using MadiumManager.Infrastructure.Servers;
using Xunit;

namespace MadiumManager.Tests;

public sealed class FileServerStateStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsConfirmedEndpoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MadiumManagerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileServerStateStore(directory);
            var expected = new ServerStateSnapshot(
                "account-1",
                "session-1",
                new ServerEndpoint(IPAddress.Parse("2001:db8::10"), 27_015),
                DateTimeOffset.UtcNow,
                ServerStateSource.MediumLog);

            await store.SaveConfirmedAsync(expected, CancellationToken.None);
            var actual = await store.LoadLastConfirmedAsync("account-1", CancellationToken.None);

            Assert.NotNull(actual);
            Assert.Equal(expected.AccountId, actual.AccountId);
            Assert.Equal(expected.SessionId, actual.SessionId);
            Assert.Equal(expected.Endpoint, actual.Endpoint);
            Assert.Equal(expected.Source, actual.Source);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
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

    [Theory]
    [InlineData(0)]
    [InlineData(65_536)]
    public void ServerEndpoint_RejectsInvalidPort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServerEndpoint(IPAddress.Loopback, port));
    }
}
