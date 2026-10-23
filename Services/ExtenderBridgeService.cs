using System.Text;
using System.Text.Json;

namespace BG3ItemManager.Services;

public sealed class BridgeCharacter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class BridgeReply
{
    public string Id { get; set; } = "";
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public List<BridgeCharacter> Characters { get; set; } = new();
}

/// <summary>
/// Host-local command/response transport. Communication goes through Script Extender's
/// sandboxed IO directory. No sockets, game memory hooks or console automation.
/// </summary>
public sealed class ExtenderBridgeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly SemaphoreSlim _oneRequest = new(1, 1);
    public string DirectoryPath { get; }
    public string RequestPath => Path.Combine(DirectoryPath, "request.json");
    public string ResponsePath => Path.Combine(DirectoryPath, "response.json");
    public string StatusPath => Path.Combine(DirectoryPath, "status.json");

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Larian Studios", "Baldur's Gate 3", "Script Extender", "BG3ItemManager");

    public ExtenderBridgeService(string? directory = null) => DirectoryPath = directory ?? DefaultDirectory;

    public bool IsBridgeAlive(TimeSpan? maxAge = null)
    {
        if (!File.Exists(StatusPath)) return false;
        try
        {
            // A recently changed file is the heartbeat. Old files from previous BG3 runs
            // are deliberately not treated as a working connection.
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(StatusPath);
            return age >= TimeSpan.Zero && age <= (maxAge ?? TimeSpan.FromSeconds(4));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public async Task<BridgeReply> SendAsync(string action, string? template = null,
        string? target = null, int quantity = 1, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (action is not ("ping" or "list_characters" or "give_item"))
            throw new ArgumentException("Unbekannter Brückenbefehl.", nameof(action));
        if (action == "give_item")
        {
            if (!Guid.TryParse(template, out _))
                throw new ArgumentException("Zum Erzeugen wird eine gültige Root-Template-UUID benötigt.", nameof(template));
            if (string.IsNullOrWhiteSpace(target) || target.Length > 150)
                throw new ArgumentException("Ein gültiger Charakter muss ausgewählt werden.", nameof(target));
            if (quantity < 1 || quantity > 99)
                throw new ArgumentOutOfRangeException(nameof(quantity), "Erlaubt sind 1 bis 99 Stück.");
        }
        await _oneRequest.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var requestId = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(new
            {
                version = 1,
                id = requestId,
                action,
                template = template ?? "",
                target = target ?? "",
                quantity,
                issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            }, JsonOptions);
            AtomicWrite(RequestPath, json);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(ResponsePath))
                    {
                        string response = await File.ReadAllTextAsync(ResponsePath, deadline.Token);
                        var parsed = JsonSerializer.Deserialize<BridgeReply>(response, JsonOptions);
                        if (parsed is not null && parsed.Id == requestId)
                            return parsed;
                    }
                }
                catch (JsonException) { /* response may be in the process of being replaced */ }
                catch (IOException) { /* next poll retries */ }
                await Task.Delay(150, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Keine Antwort vom Script Extender. BG3-Spielstand und Lua-Brücke prüfen.");
        }
        finally { _oneRequest.Release(); }
    }

    private static void AtomicWrite(string path, string content)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
