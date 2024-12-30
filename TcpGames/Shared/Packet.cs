using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared;

public class Packet
{
    [JsonPropertyName("command")]
    public string Command { get; init; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; }

    public Packet(string command = "", string message = "")
    {
        Command = command;
        Message = message;
    }

    public override string ToString()
    {
        return $"[Packet:\n  Command=`{Command}`\n  Message=`{Message}`";
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }

    public static Packet? FromJson(string jsonData)
    {
        return JsonSerializer.Deserialize<Packet>(jsonData);
    }
}