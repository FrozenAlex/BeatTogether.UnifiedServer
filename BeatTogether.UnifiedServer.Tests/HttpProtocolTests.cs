using System.Net;
using System.Text;
using BeatTogether.Core.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BeatTogether.UnifiedServer.Tests;

public sealed class HttpProtocolTests
{
    private static async Task<IHost> StartHost()
    {
        var host = Program.CreateUnifiedServer(Array.Empty<string>())
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Status:MinimumAppVersion"] = "1.45.3",
                ["ServerConfiguration:AuthenticateClients"] = "false",
                ["ServerConfiguration:VersionRanges:0:MinVersion"] = "1.45.3",
                ["ServerConfiguration:VersionRanges:0:MaxVersion"] = "1.45.3"
            }))
            .ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton(Mock.Of<ILayer2>())))
            .ConfigureWebHost(web => web.UseTestServer().ConfigureTestServices(s => {
                // TestServer has no remote socket. Supply a loopback address as Kestrel would.
                s.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, LoopbackFilter>();
            })).Build();
        await host.StartAsync();
        return host;
    }

    private sealed class LoopbackFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next)
            => app => { Microsoft.AspNetCore.Builder.UseExtensions.Use(app, (ctx, nextMiddleware) => {
                ctx.Connection.RemoteIpAddress = IPAddress.Loopback; return nextMiddleware();
            }); next(app); };
    }

    [Theory]
    [InlineData("/status")]
    [InlineData("/beat_saber_multiplayer_status?service_environment=Production")]
    public async Task StatusRoutesAdvertise1453(string route)
    {
        using var host = await StartHost();
        var response = await host.GetTestClient().GetAsync(route);
        response.EnsureSuccessStatusCode();
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("1.45.3", (string?)body["minimum_app_version"]);
    }

    [Theory]
    [InlineData("1.40.8_7379", 51)]
    [InlineData("1.42.0", 51)]
    [InlineData("1.45.2", 51)]
    [InlineData("1.45.4", 52)]
    [InlineData("garbage", 50)]
    [InlineData(null, 50)]
    public async Task RejectsOtherSchemasBeforeAuthentication(string? version, int error)
    {
        using var host = await StartHost();
        var body = new JObject { ["version"] = version, ["ticket_id"] = "existing-ticket", ["private_game_code"] = "ABCD" };
        var response = await host.GetTestClient().PostAsync("/beat_saber_get_multiplayer_instance",
            new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var result = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(error, (int?)result["error_code"]);
        Assert.Equal("existing-ticket", (string?)result["ticket_id"]);
    }

    [Theory]
    [InlineData(3, "01020304", "76561198000000000")]
    [InlineData(2, "test-oculus-proof", "12345")]
    public async Task LegacyCredentialsReachMatchmaking(int platform, string proof, string rawId)
    {
        using var host = await StartHost();
        var request = new JObject {
            ["version"] = "1.45.3_30054", ["platform"] = platform, ["user_id"] = "hashed-player-id",
            ["auth_user_id"] = rawId, ["single_use_auth_token"] = proof, ["private_game_code"] = "ABCD"
        };
        var response = await host.GetTestClient().PostAsync("/beat_saber_get_multiplayer_instance",
            new StringContent(request.ToString(), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var result = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, (int?)result["error_code"]); // Reached missing-room lookup (not auth/version rejection).
        Assert.StartsWith("ps:bt$", (string?)result["ticket_id"]);
    }
}
