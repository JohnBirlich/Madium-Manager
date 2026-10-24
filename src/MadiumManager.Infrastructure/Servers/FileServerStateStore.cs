using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MadiumManager.Application.Servers;
using MadiumManager.Domain.Servers;

namespace MadiumManager.Infrastructure.Servers;

/// <summary>
/// Small single-writer store for confirmed reconnect state. The temporary file is
/// written and flushed in the destination directory, then atomically renamed.
/// Account secrets do not belong in this store.
/// </summary>
public sealed class FileServerStateStore : IServerStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileServerStateStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("State directory is required.", nameof(directory));
        }

        _directory = directory;
    }

    public async Task<ServerStateSnapshot?> LoadLastConfirmedAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        var path = GetPath(accountId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var document = await JsonSerializer.DeserializeAsync<ServerStateDocument>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            return ToDomain(document, accountId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveConfirmedAsync(
        ServerStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var path = GetPath(snapshot.AccountId);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    ServerStateDocument.FromDomain(snapshot),
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // Both paths are in the same directory/volume. The per-store gate prevents
            // load/write and write/write races inside this application instance.
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string GetPath(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new ArgumentException("Account id is required.", nameof(accountId));
        }

        // Hashing avoids path traversal, invalid file names and account-id disclosure.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountId)));
        return Path.Combine(_directory, key + ".json");
    }

    private static ServerStateSnapshot ToDomain(ServerStateDocument? document, string accountId)
    {
        if (document is null ||
            !string.Equals(document.AccountId, accountId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(document.SessionId) ||
            !IPAddress.TryParse(document.Address, out var address))
        {
            throw new InvalidDataException("The confirmed server-state file is invalid.");
        }

        if (!Enum.TryParse<ServerStateSource>(document.Source, ignoreCase: false, out var source) ||
            !Enum.IsDefined(source))
        {
            throw new InvalidDataException("The confirmed server-state source is invalid.");
        }

        return new ServerStateSnapshot(
            document.AccountId,
            document.SessionId,
            new ServerEndpoint(address, document.Port),
            document.CapturedAt,
            source);
    }

    private sealed record ServerStateDocument(
        string AccountId,
        string SessionId,
        string Address,
        int Port,
        DateTimeOffset CapturedAt,
        string Source)
    {
        public static ServerStateDocument FromDomain(ServerStateSnapshot snapshot) => new(
            snapshot.AccountId,
            snapshot.SessionId,
            snapshot.Endpoint.Address.ToString(),
            snapshot.Endpoint.Port,
            snapshot.CapturedAt,
            snapshot.Source.ToString());
    }
}
