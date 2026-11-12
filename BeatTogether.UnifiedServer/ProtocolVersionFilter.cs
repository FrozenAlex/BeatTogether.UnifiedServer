using BeatTogether.MasterServer.Api.HttpControllers;
using BeatTogether.MasterServer.Messaging.Enums;
using BeatTogether.MasterServer.Messaging.Models.HttpApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BeatTogether.UnifiedServer;

// This build has one dedicated-server wire schema. Reject incompatible clients
// before authentication or session allocation, including resumed HTTP sessions.
public sealed class ProtocolVersionFilter : IAsyncActionFilter
{
    private static readonly Version SupportedVersion = new(1, 45, 3);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.Controller is GetMultiplayerInstanceController &&
            context.ActionArguments.Values.OfType<GetMultiplayerInstanceRequest>().FirstOrDefault() is { } request)
        {
            var versionText = request.Version?.Split('_')[0];
            var error = !Version.TryParse(versionText, out var version)
                ? MultiplayerPlacementErrorCode.GameVersionUnknown
                : version < SupportedVersion ? MultiplayerPlacementErrorCode.GameVersionTooOld
                : version > SupportedVersion ? MultiplayerPlacementErrorCode.GameVersionTooNew
                : MultiplayerPlacementErrorCode.Success;
            if (error != MultiplayerPlacementErrorCode.Success)
            {
                var response = new GetMultiplayerInstanceResponse { ErrorCode = error };
                response.AddRequestContext(request);
                context.Result = new JsonResult(response);
                return;
            }
        }
        await next();
    }
}
