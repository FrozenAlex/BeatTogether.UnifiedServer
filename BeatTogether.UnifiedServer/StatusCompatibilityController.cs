using BeatTogether.Status.Api.Controllers.Controllers;
using BeatTogether.Status.Api.Controllers.Models;
using Microsoft.AspNetCore.Mvc;

namespace BeatTogether.UnifiedServer;

[ApiController]
public sealed class StatusCompatibilityController : ControllerBase
{
    private readonly StatusController _status;
    public StatusCompatibilityController(StatusController status) => _status = status;

    // 1.45 derives this route from the Graph base URL; /status remains available
    // for the BeatTogether server selector and existing server configurations.
    [HttpGet("beat_saber_multiplayer_status")]
    public MasterServerStatusData Get() => _status.Get();
}
