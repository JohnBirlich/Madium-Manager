using System.Text.Json;
using MadiumManager.Application.Accounts;
using MadiumManager.Domain.Accounts;

namespace MadiumManager.Infrastructure.Accounts;

/// <summary>
/// Atomic JSON repository for non-secret account metadata. It is intentionally
/// small; SQLite can replace it without changing the desktop view models.
/// </summary>
public sealed class JsonAccountRepository : IAccountRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonAccountRepository(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Account repository path is required.", nameof(path));
        }

        _path = path;
    }

    public async Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<AccountProfile>();
            }

            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var documents = await JsonSerializer.DeserializeAsync<List<AccountDocument>>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            return (documents ?? [])
                .Select(document => document.ToDomain())
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<AccountProfile> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var directory = Path.GetDirectoryName(_path) ??
            throw new InvalidOperationException("Account repository directory is invalid.");
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            var documents = accounts.Select(AccountDocument.FromDomain).ToArray();

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
                    documents,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
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

    private sealed record AccountDocument(
        string Id,
        string DisplayName,
        string Login,
        string Note,
        DateTimeOffset CreatedAt)
    {
        public static AccountDocument FromDomain(AccountProfile account) => new(
            account.Id,
            account.DisplayName,
            account.Login,
            account.Note,
            account.CreatedAt);

        public AccountProfile ToDomain()
        {
            if (string.IsNullOrWhiteSpace(Id) ||
                string.IsNullOrWhiteSpace(DisplayName) ||
                string.IsNullOrWhiteSpace(Login))
            {
                throw new InvalidDataException("An account repository entry is invalid.");
            }

            return new AccountProfile(Id, DisplayName, Login, Note ?? string.Empty, CreatedAt);
        }
    }
}
