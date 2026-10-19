using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheOrder.Application;

namespace TheOrder.Platform.Windows;

public sealed class WindowsPersonalSyncStore(NativeConfiguration configuration) : IPersonalSyncStore
{
    private sealed record Envelope(int Schema, Guid ProfileId, Guid StoreId, string WindowsUser, AccountSyncState State);
    private static string WindowsUser()
    { using var identity = System.Security.Principal.WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new OnlineException(OnlineFailure.CredentialStorage); }
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, Converters = { new JsonStringEnumConverter() } };
    private readonly SemaphoreSlim serial = new(1, 1);
    private (NativeProfileStore Profile, string Path) Location(string account, string client)
    {
        if (account.Length is < 1 or > 256 || client.Length is < 1 or > 256 || account.Any(char.IsControl) || client.Any(char.IsControl)) throw new OnlineException(OnlineFailure.InvalidRequest);
        var profile = configuration.Profile as NativeProfileStore ?? throw new OnlineException(OnlineFailure.CredentialStorage);
        profile.ValidateAccess(); var root = NativeProfileAccess.Validate(configuration);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\n" + client)));
        return (profile, Path.Combine(root, "native-nextmoe-sync-" + id + ".json"));
    }
    public async Task<AccountSyncState?> LoadAsync(string accountId, string clientId, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var location = Location(accountId, clientId); using var file = OpenOptional(location.Path); if (file is null) return null;
            if (file.Length is < 2 or > 8388608) throw new OnlineException(OnlineFailure.CredentialStorageCorrupt);
            var bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<Envelope>(bytes, Json) ?? throw new JsonException();
            if (envelope.Schema != 2 || envelope.WindowsUser != WindowsUser() || envelope.ProfileId != location.Profile.Manifest.ProfileId || envelope.StoreId != location.Profile.Manifest.StoreId
                || envelope.State is null || envelope.State.AccountId != accountId || envelope.State.ClientId != clientId) throw new JsonException();
            Validate(envelope.State); return envelope.State;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { throw new OnlineException(OnlineFailure.CredentialStorageCorrupt); }
        finally { serial.Release(); }
    }
    public async Task SaveAsync(AccountSyncState state, CancellationToken cancellation)
    {
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            Validate(state); cancellation.ThrowIfCancellationRequested(); var location = Location(state.AccountId, state.ClientId);
            var held = IsolatedLibraryPersistence.PinDirectoryChain(location.Profile.Root);
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(2, location.Profile.Manifest.ProfileId, location.Profile.Manifest.StoreId, WindowsUser(), state), Json);
                if (bytes.Length > 8388608) throw new OnlineException(OnlineFailure.CredentialStorage);
                location.Profile.ValidateAccess(); cancellation.ThrowIfCancellationRequested();
                var receipt = await WindowsAtomicFile.WriteAsync(location.Path, bytes).ConfigureAwait(false);
                if (receipt.Warning is not null) throw new OnlineException(OnlineFailure.CredentialStorage);
            }
            finally { foreach (var handle in held) handle.Dispose(); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new OnlineException(OnlineFailure.CredentialStorage); }
        finally { serial.Release(); }
    }
    private static void Validate(AccountSyncState state)
    {
        if (state is null || !Safe(state.AccountId, 256) || !Safe(state.ClientId, 256) || state.Settings is null || !Enum.IsDefined(state.Settings.Playtime) || !Enum.IsDefined(state.Settings.State) || !Enum.IsDefined(state.Settings.Folders)
            || state.Works is null || state.Folders is null || state.Imported is null || state.Pending is null || state.Works.Length > 20000
            || state.Folders.Length > 200 || state.Imported.Length > 20000 || state.Pending.Length > 500 || state.FolderCreations?.Length > 1
            || state.Works.Any(x => x is null || !Safe(x.EntryId, 200) || !Id(x.WorkId) || x.LocalSeconds < 0 || x.CloudMinutes < 0 || x.AgreedMinutes < 0 || x.CarriedSeconds is < 0 or > 59
                || x.SessionIds is null || x.SessionIds.Length > 100000 || x.SessionIds.Any(s => !Safe(s, 200)) || !Date(x.UpdatedAt) || !LocalState(x.LocalState) || !CloudState(x.CloudState, x.Completion))
            || state.Folders.Any(x => x is null || !Id(x.FolderId) || !Safe(x.LocalCollectionId, 200) || !Safe(x.Name, 100) || x.Members is null || x.CloudBaseline is null
                || x.Members.Length > 10000 || x.CloudBaseline.Length > 10000 || x.Members.Any(s => !Id(s)) || x.CloudBaseline.Any(s => !Id(s)) || !Cursor(x.Cursor) || x.UpdatedAt is not null && !Date(x.UpdatedAt))
            || state.Imported.Any(x => x is null || !Id(x.WorkId) || x.Minutes < 0 || x.Rating is not (null or >= 1 and <= 10) || !CloudState(x.State, x.Completion))
            || state.Pending.Any(x => x is null || !Guid.TryParseExact(x.Id, "D", out _) || !Enum.IsDefined(x.Kind) || x.Kind == PersonalSyncKind.Rating || !Date(x.CreatedAt)
                || x.Items is null || x.Items.Length is < 1 or > 100 || x.Items.Any(y => !Write(y, x.Kind, false)) || x.Items.Select(y => y.WorkId).Distinct().Count() != x.Items.Length
                || x.Before is null || x.Before.Length != x.Items.Length || x.Before.Any(y => !Write(y, x.Kind, true)) || x.Before.Select(y => y.WorkId).Except(x.Items.Select(y => y.WorkId)).Any()
                || x.Kind == PersonalSyncKind.Folder && !Id(x.FolderId))
            || (state.FolderCreations ?? []).Any(x => x is null || !Guid.TryParseExact(x.Id, "D", out _) || !Safe(x.Name, 100) || !Date(x.CreatedAt) || x.LocalMembers is null
                || x.LocalMembers.Length > 10000 || x.LocalMembers.Any(y => !Id(y)))
            || state.Works.GroupBy(x => x.EntryId).Any(x => x.Count() > 1) || state.Folders.GroupBy(x => x.FolderId).Any(x => x.Count() > 1)
            || state.Imported.GroupBy(x => x.WorkId).Any(x => x.Count() > 1) || state.Pending.GroupBy(x => x.Id).Any(x => x.Count() > 1)
            || !Cursor(state.PlaytimeCursor) || !Cursor(state.StateCursor) || !Cursor(state.RatingCursor) || state.LastSuccess is not null && !Date(state.LastSuccess))
            throw new OnlineException(OnlineFailure.CredentialStorageCorrupt);
    }
    private static bool Safe(string? value, int max) => value is { Length: > 0 } && value.Length <= max && !value.Any(char.IsControl);
    private static bool Id(string? value) => value is { Length: > 0 and <= 20 } && value.All(char.IsAsciiDigit);
    private static bool Date(string value) => value is { Length: > 0 and <= 40 } && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
    private static bool Cursor(string? value) => value is null || value.Length is > 4 and <= 512 && value.StartsWith("cur_", StringComparison.Ordinal)
        && value[4..].All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '~' or '-');
    private static bool LocalState(string value) => TheOrder.Domain.GameProgress.Valid(value);
    private static bool CloudState(string? value, string? completion) => value is null ? completion is null : value is "wish" or "doing" or "done" or "on_hold" or "dropped"
        && completion is null or "one_route" or "main" or "all" && (value != "wish" || completion is null);
    private static bool Write(PersonalWrite? value, PersonalSyncKind kind, bool before) => value is not null && Id(value.WorkId) && (kind switch {
        PersonalSyncKind.Playtime => before ? value.Minutes is null or >= 0 : value.Minutes is >= 0 and <= 60000,
        PersonalSyncKind.State => CloudState(value.State, value.Completion) && (before || value.State is not null),
        PersonalSyncKind.Folder => true, _ => false });
    private static Stream? OpenOptional(string path) { try { return IsolatedLibrarySource.OpenChecked(path); } catch (FileNotFoundException) { return null; } }
}
