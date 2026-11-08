using System.Text.Json;

namespace YMH219.ChatApp;

internal sealed record ChatPacket(string Type, string Username = "", string Text = "", DateTimeOffset? Timestamp = null);

internal static class ChatProtocol
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ChatPacket packet) => JsonSerializer.Serialize(packet, Options);

    public static ChatPacket? Deserialize(string json) => JsonSerializer.Deserialize<ChatPacket>(json, Options);

    public static ChatPacket Create(string type, string username = "", string text = "") =>
        new(type, username, text, DateTimeOffset.UtcNow);
}
