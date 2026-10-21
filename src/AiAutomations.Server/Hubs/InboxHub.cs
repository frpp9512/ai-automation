using Microsoft.AspNetCore.SignalR;

namespace AiAutomations.Server.Hubs;

/// <summary>Push-only hub: the server broadcasts, clients just listen. See IRealtimeNotifier.</summary>
public class InboxHub : Hub;
