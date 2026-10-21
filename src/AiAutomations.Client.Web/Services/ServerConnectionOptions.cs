namespace AiAutomations.Client.Web.Services;

public class ServerConnectionOptions
{
    public const string SectionName = "Server";

    public required string BaseUrl { get; set; }
    public required string ApiKey { get; set; }
}
