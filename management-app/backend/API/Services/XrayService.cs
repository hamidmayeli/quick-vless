using API.Models;
using Google.Protobuf;
using Grpc.Net.Client;
using Xray.App.Proxyman.Command;
using Xray.App.Stats.Command;
using Xray.Common.Serial;
using Xray.Proxy.Vless;

namespace API.Services;

public sealed class XrayService : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly StatsService.StatsServiceClient _statsClient;
    private readonly HandlerService.HandlerServiceClient _handlerClient;
    private readonly string _inboundTag;
    private readonly ILogger<XrayService> _logger;

    public XrayService(IConfiguration config, ILogger<XrayService> logger)
    {
        _logger = logger;
        var statsServer = config["Xray:StatsServer"] ?? "127.0.0.1:10085";
        _inboundTag = config["Xray:InboundTag"] ?? "vless-in";
        _channel = GrpcChannel.ForAddress($"http://{statsServer}");
        _statsClient = new StatsService.StatsServiceClient(_channel);
        _handlerClient = new HandlerService.HandlerServiceClient(_channel);
    }

    public async Task<bool> AddUserAsync(User user)
    {
        try
        {
            var account = new Account { Id = user.Secret, Flow = "xtls-rprx-vision" };
            var protoUser = new Xray.Common.Protocol.User
            {
                Email = user.Secret,
                Account = new TypedMessage
                {
                    Type = "xray.proxy.vless.Account",
                    Value = ByteString.CopyFrom(account.ToByteArray()),
                }
            };
            var addOp = new AddUserOperation { User = protoUser };
            await _handlerClient.AlterInboundAsync(new AlterInboundRequest
            {
                Tag = _inboundTag,
                Operation = new TypedMessage
                {
                    Type = "xray.app.proxyman.command.AddUserOperation",
                    Value = ByteString.CopyFrom(addOp.ToByteArray()),
                }
            });
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
        {
            _logger.LogWarning("Xray gRPC unavailable — AddUser skipped for user {Id}", user.Id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddUser gRPC call failed for user {Id}", user.Id);
            return false;
        }
    }

    public async Task<bool> RemoveUserAsync(User user)
    {
        try
        {
            var removeOp = new RemoveUserOperation { Email = user.Secret };
            await _handlerClient.AlterInboundAsync(new AlterInboundRequest
            {
                Tag = _inboundTag,
                Operation = new TypedMessage
                {
                    Type = "xray.app.proxyman.command.RemoveUserOperation",
                    Value = ByteString.CopyFrom(removeOp.ToByteArray()),
                }
            });
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
        {
            _logger.LogWarning("Xray gRPC unavailable — RemoveUser skipped for user {Id}", user.Id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoveUser gRPC call failed for user {Id}", user.Id);
            return false;
        }
    }

    public async Task<Dictionary<string, long>> QueryStatsAsync()
    {
        try
        {
            var response = await _statsClient.QueryStatsAsync(new QueryStatsRequest
            {
                Pattern = "user",
                Reset = true,
            });
            return ParseStats(response.Stat);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
        {
            _logger.LogWarning("Xray gRPC unavailable — QueryStats returned empty");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QueryStats gRPC call failed");
            return [];
        }
    }

    public static Dictionary<string, long> ParseStats(IEnumerable<Stat> stats)
    {
        var result = new Dictionary<string, long>();
        foreach (var stat in stats)
        {
            // stat.Name format: "user>>>email>>>traffic>>>uplink" or ">>>downlink"
            var parts = stat.Name.Split(">>>", StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;
            var email = parts[1];
            result.TryGetValue(email, out var existing);
            result[email] = existing + stat.Value;
        }
        return result;
    }

    public void Dispose() => _channel.Dispose();
}
