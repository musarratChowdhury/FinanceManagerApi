using FinanceManagerApi.Services.Storage;

namespace FinanceManagerApi.Moq;

/// <summary>
/// In-memory stand-in for the receipt-photo bucket. Records what was written and
/// removed so the ordering rules (object first, row second) can be asserted
/// without reaching for S3.
/// </summary>
internal sealed class FakeReceiptStorage : IReceiptStorage
{
    public bool IsConfigured { get; set; } = true;

    public List<StoredObject> Stored { get; } = new();

    public List<string> DeletedKeys { get; } = new();

    /// <summary>Every operation in call order: "put", "delete".</summary>
    public List<string> Calls { get; } = new();

    public Exception? PutFailure { get; set; }

    public Exception? DeleteFailure { get; set; }

    public TimeSpan? LastSignedTtl { get; private set; }

    public StoredObject? LastStored => Stored.Count == 0 ? null : Stored[^1];

    public async Task PutAsync(
        string key,
        Stream data,
        long length,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("put");

        if (PutFailure is not null)
        {
            throw PutFailure;
        }

        using var buffer = new MemoryStream();
        await data.CopyToAsync(buffer, cancellationToken);

        // The caller passes the length explicitly, so a mismatch is a bug worth seeing.
        Assert.Equal(length, buffer.Length);

        Stored.Add(new StoredObject(key, buffer.ToArray(), contentType));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Calls.Add("delete");

        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        DeletedKeys.Add(key);
        return Task.CompletedTask;
    }

    public string GetSignedUrl(string key, TimeSpan ttl)
    {
        Calls.Add("sign");
        LastSignedTtl = ttl;
        return $"https://storage.test/{key}?expires={ttl.TotalSeconds:0}";
    }

    internal sealed record StoredObject(string Key, byte[] Bytes, string ContentType);
}
