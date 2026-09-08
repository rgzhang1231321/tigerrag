using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TigerRAG.Application.Security;

namespace TigerRAG.Api.Hubs;

[Authorize(Policy = SystemPermissions.UseChat)]
public sealed class ChatHub : Hub;
