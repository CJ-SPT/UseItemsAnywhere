using Fika.Core.Networking.LiteNetLib.Utils;

namespace UseItemsAnywhereFika;

public struct RulesRequestPacket : INetSerializable
{
    public int Protocol;
    public string CoreVersion;
    public string RequestId;
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Protocol);
        writer.Put(CoreVersion);
        writer.Put(RequestId);
    }
    public void Deserialize(NetDataReader reader)
    {
        Protocol = reader.GetInt();
        CoreVersion = reader.GetString(32);
        RequestId = reader.GetString(32);
    }
}

public struct RulesReplyPacket : INetSerializable
{
    public int Protocol;
    public string CoreVersion;
    public string RequestId;
    public string RulesJson;
    public string Error;
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Protocol);
        writer.Put(CoreVersion);
        writer.Put(RequestId);
        writer.Put(RulesJson);
        writer.Put(Error);
    }
    public void Deserialize(NetDataReader reader)
    {
        Protocol = reader.GetInt();
        CoreVersion = reader.GetString(32);
        RequestId = reader.GetString(32);
        RulesJson = reader.GetString(RulesCodec.MaximumJsonLength);
        Error = reader.GetString(512);
    }
}
