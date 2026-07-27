// Strongly-typed identifiers. These exist to make the following impossible to compile:
//
//     repo.GetByIdAsync(userId, sessionId)   // arguments transposed
//
// which, with two adjacent Guid parameters, previously compiled and silently returned null.
//
// Each type serialises as a bare GUID string and parses from one, so the HTTP wire format and
// the Table Storage PartitionKey/RowKey representation are byte-identical to the raw Guid.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoMemeVideo.Shared.Domain;

/// <summary>Identifies a single video-processing session.</summary>
[JsonConverter(typeof(SessionIdJsonConverter))]
public readonly record struct SessionId(Guid Value) : IParsable<SessionId>
{
    public static SessionId New() => new(Guid.NewGuid());
    public static readonly SessionId Empty = new(Guid.Empty);

    public override string ToString() => Value.ToString();

    public static SessionId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out SessionId result)
    {
        if (Guid.TryParse(s, out var guid)) { result = new SessionId(guid); return true; }
        result = default;
        return false;
    }
}

/// <summary>Identifies an authenticated (or anonymous/guest) user.</summary>
[JsonConverter(typeof(UserIdJsonConverter))]
public readonly record struct UserId(Guid Value) : IParsable<UserId>
{
    public static UserId New() => new(Guid.NewGuid());
    public static readonly UserId Empty = new(Guid.Empty);

    public override string ToString() => Value.ToString();

    public static UserId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out UserId result)
    {
        if (Guid.TryParse(s, out var guid)) { result = new UserId(guid); return true; }
        result = default;
        return false;
    }
}

/// <summary>Identifies a meme sound in the library.</summary>
[JsonConverter(typeof(SoundIdJsonConverter))]
public readonly record struct SoundId(Guid Value) : IParsable<SoundId>
{
    public static SoundId New() => new(Guid.NewGuid());
    public static readonly SoundId Empty = new(Guid.Empty);

    public override string ToString() => Value.ToString();

    public static SoundId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out SoundId result)
    {
        if (Guid.TryParse(s, out var guid)) { result = new SoundId(guid); return true; }
        result = default;
        return false;
    }
}

/// <summary>Identifies a single cut within a director script.</summary>
[JsonConverter(typeof(EntryIdJsonConverter))]
public readonly record struct EntryId(Guid Value) : IParsable<EntryId>
{
    public static EntryId New() => new(Guid.NewGuid());
    public static readonly EntryId Empty = new(Guid.Empty);

    public override string ToString() => Value.ToString();

    public static EntryId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, IFormatProvider? provider, out EntryId result)
    {
        if (Guid.TryParse(s, out var guid)) { result = new EntryId(guid); return true; }
        result = default;
        return false;
    }
}

// ── JSON converters: read/write the bare GUID string, never an object wrapper ──

public sealed class SessionIdJsonConverter : JsonConverter<SessionId>
{
    public override SessionId Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, SessionId value, JsonSerializerOptions _)
        => writer.WriteStringValue(value.Value);
}

public sealed class UserIdJsonConverter : JsonConverter<UserId>
{
    public override UserId Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, UserId value, JsonSerializerOptions _)
        => writer.WriteStringValue(value.Value);
}

public sealed class SoundIdJsonConverter : JsonConverter<SoundId>
{
    public override SoundId Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, SoundId value, JsonSerializerOptions _)
        => writer.WriteStringValue(value.Value);
}

public sealed class EntryIdJsonConverter : JsonConverter<EntryId>
{
    public override EntryId Read(ref Utf8JsonReader reader, Type _, JsonSerializerOptions __)
        => new(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, EntryId value, JsonSerializerOptions _)
        => writer.WriteStringValue(value.Value);
}
