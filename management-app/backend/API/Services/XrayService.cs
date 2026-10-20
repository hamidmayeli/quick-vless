using System.Text.RegularExpressions;
using API.Models;
using Google.Protobuf;
using Grpc.Net.Client;
using Xray.App.Proxyman.Command;
using Xray.App.Stats.Command;
using Xray.Common.Serial;
using Xray.Proxy.Vless;

namespace API.Services;

/// <summary>The Xray gRPC calls that change users on a running Xray.</summary>
public interface IXrayClient
{
    Task<bool> AddUserAsync(User user, string tag, string flow);
    Task<bool> RemoveUserAsync(string userId, string secret, string tag);

    /// <summary>True when Xray's API answers. Does not reset the usage counters.</summary>
    Task<bool> ProbeAsync();
}

public sealed partial class XrayService : IXrayClient, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly StatsService.StatsServiceClient _statsClient;
    private readonly HandlerService.HandlerServiceClient _handlerClient;
    private readonly ILogger<XrayService> _logger;

    // Xray reports these as plain errors; they mean the user is already in the desired state.
    [GeneratedRegex(@"User .* already exists", RegexOptions.IgnoreCase)]
    private static partial Regex AlreadyExists();

    [GeneratedRegex(@"User .* not found", RegexOptions.IgnoreCase)]
    private static partial Regex UserNotFound();

    public XrayService(IConfiguration config, ILogger<XrayService> logger)
    {
        _logger = logger;
        var statsServer = config["Xray:StatsServer"] ?? "127.0.0.1:10085";
        _channel = GrpcChannel.ForAddress($"http://{statsServer}");
        _statsClient = new StatsService.StatsServiceClient(_channel);
        _handlerClient = new HandlerService.HandlerServiceClient(_channel);
    }

    public async Task<bool> AddUserAsync(User user, string tag, string flow)
    {
        try
        {
            var account = new Account { Id = user.Secret, Flow = flow };
            var protoUser = new global::Xray.Common.Protocol.User
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
                Tag = tag,
                Operation = new TypedMessage
                {
                    Type = "xray.app.proxyman.command.AddUserOperation",
                    Value = ByteString.CopyFrom(addOp.ToByteArray()),
                }
            });
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (AlreadyExists().IsMatch(ex.Status.Detail))
        {
            _logger.LogDebug("User {Id} is already on inbound {Tag}", user.Id, tag);
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
        {
            _logger.LogWarning("Xray gRPC unavailable — AddUser skipped for user {Id}", user.Id);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddUser gRPC call failed for user {Id} on inbound {Tag}", user.Id, tag);
            return false;
        }
    }

    public async Task<bool> RemoveUserAsync(string userId, string secret, string tag)
    {
        try
        {
            var removeOp = new RemoveUserOperation { Email = secret };
            await _handlerClient.AlterInboundAsync(new AlterInboundRequest
            {
                Tag = tag,
                Operation = new TypedMessage
                {
                    Type = "xray.app.proxyman.command.RemoveUserOperation",
                    Value = ByteString.CopyFrom(removeOp.ToByteArray()),
                }
            });
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (UserNotFound().IsMatch(ex.Status.Detail))
        {
            _logger.LogDebug("User {Id} was not on inbound {Tag}", userId, tag);
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
        {
            _logger.LogWarning("Xray gRPC unavailable — RemoveUser skipped for user {Id}", userId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RemoveUser gRPC call failed for user {Id} on inbound {Tag}", userId, tag);
            return false;
        }
    }

    public async Task<bool> ProbeAsync()
    {
        try
        {
            await _statsClient.QueryStatsAsync(
                new QueryStatsRequest { Pattern = "__probe__", Reset = false },
                deadline: DateTime.UtcNow.AddSeconds(3));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Xray gRPC probe failed");
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
