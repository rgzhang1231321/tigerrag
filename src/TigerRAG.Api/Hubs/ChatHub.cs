using Microsoft.AspNetCore.SignalR;

namespace TigerRAG.Api.Hubs;

/// <summary>问答 SignalR Hub。Hub 自身只做连接认证与推送编排，业务逻辑在 <see cref="TigerRAG.Application.Chat.ChatService"/>。</summary>
public sealed class ChatHub : Hub;
