using System;
using Newtonsoft.Json;
using UseItemsAnywhere;
using UseItemsAnywhere.Integration;

namespace UseItemsAnywhereFika;

public static class RulesCodec
{
    public const int ProtocolVersion = 1;
    public const int MaximumJsonLength = 16384;
    private static readonly JsonSerializerSettings Settings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MissingMemberHandling = MissingMemberHandling.Error,
        MaxDepth = 8,
    };

    public static string Serialize(GameplayRules rules) => JsonConvert.SerializeObject(rules.ToData(), Settings);

    public static GameplayRules DecodeReply(RulesReplyPacket reply)
    {
        if (reply.Protocol != ProtocolVersion || reply.CoreVersion != Plugin.PluginVersion)
            throw new ArgumentException("Use Items Anywhere core/addon versions do not match the host.");
        if (!string.IsNullOrEmpty(reply.Error)) throw new ArgumentException(reply.Error);
        return Deserialize(reply.RulesJson);
    }

    public static GameplayRules Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaximumJsonLength)
            throw new ArgumentException("Invalid rules payload size.");
        var data = JsonConvert.DeserializeObject<GameplayRulesData>(json, Settings)
            ?? throw new ArgumentException("Missing rules payload.");
        return new GameplayRules(data);
    }
}
