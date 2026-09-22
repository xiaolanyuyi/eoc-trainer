using System.Text.Json.Serialization;

namespace EocTrainer.Core;

/// <summary>Command document written to <c>Osiris Data\EocTrainer\command.json</c>.</summary>
public sealed class CommandDoc
{
    /// <summary>Random value identifying one run of the trainer app; the mod uses it to ignore stale commands.</summary>
    [JsonPropertyName("token")] public string Token { get; set; } = "";

    /// <summary>Monotonically increasing sequence number.</summary>
    [JsonPropertyName("seq")] public int Seq { get; set; }

    /// <summary>Host character guid, used as the default target.</summary>
    [JsonPropertyName("host")] public string? Host { get; set; }

    [JsonPropertyName("ops")] public List<Op> Ops { get; set; } = new();
}

/// <summary>One operation inside a command document.</summary>
public sealed class Op
{
    [JsonPropertyName("op")] public string OpName { get; set; } = "";

    [JsonPropertyName("target")] public string? Target { get; set; }

    /// <summary>Point pool: attribute, combatAbility, civilAbility, talent, source or actionPoint.</summary>
    [JsonPropertyName("kind")] public string? Kind { get; set; }

    /// <summary>Attribute / ability / talent / stat field name.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("value")] public double? Value { get; set; }

    [JsonPropertyName("amount")] public double? Amount { get; set; }

    [JsonPropertyName("keys")] public List<string>? Keys { get; set; }

    [JsonPropertyName("fields")] public List<string>? Fields { get; set; }

    [JsonPropertyName("code")] public string? Code { get; set; }
}

/// <summary>State document written by the mod to <c>Osiris Data\EocTrainer\state.json</c>.</summary>
public sealed class StateDoc
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("inGame")] public bool InGame { get; set; }
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("timestamp")] public double Timestamp { get; set; }
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = new();
    [JsonPropertyName("results")] public List<OpResult> Results { get; set; } = new();
    [JsonPropertyName("party")] public List<PartyMember> Party { get; set; } = new();
    [JsonPropertyName("debug")] public Dictionary<string, string?> Debug { get; set; } = new();
}

public sealed class OpResult
{
    [JsonPropertyName("op")] public string? Op { get; set; }
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
}

public sealed class PartyMember
{
    [JsonPropertyName("guid")] public string Guid { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("isHost")] public bool IsHost { get; set; }
    [JsonPropertyName("dead")] public bool Dead { get; set; }
    [JsonPropertyName("level")] public double? Level { get; set; }
    [JsonPropertyName("experience")] public double? Experience { get; set; }
    [JsonPropertyName("gold")] public double? Gold { get; set; }

    [JsonPropertyName("points")] public Dictionary<string, double?> Points { get; set; } = new();

    /// <summary>The raw values stored in the player's upgrade data (what the trainer writes).</summary>
    [JsonPropertyName("upgradeAttributes")] public Dictionary<string, double?> UpgradeAttributes { get; set; } = new();

    [JsonPropertyName("customUpgrade")] public bool CustomUpgrade { get; set; }

    [JsonPropertyName("attributes")] public Dictionary<string, double?> Attributes { get; set; } = new();
    [JsonPropertyName("baseAttributes")] public Dictionary<string, double?> BaseAttributes { get; set; } = new();
    [JsonPropertyName("abilities")] public Dictionary<string, double?> Abilities { get; set; } = new();
    [JsonPropertyName("baseAbilities")] public Dictionary<string, double?> BaseAbilities { get; set; } = new();
    [JsonPropertyName("talents")] public List<string> Talents { get; set; } = new();
    [JsonPropertyName("vitals")] public Dictionary<string, double?> Vitals { get; set; } = new();

    public string Display => string.IsNullOrWhiteSpace(Name) ? ShortGuid : Name!;

    public string ShortGuid => Guid.Length > 8 ? Guid[..8] : Guid;
}
