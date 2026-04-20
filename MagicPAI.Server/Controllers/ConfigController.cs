// MagicPAI.Server/Controllers/ConfigController.cs
// Lightweight config-exposure endpoint. Studio's TemporalUiUrlBuilder fetches
// /api/config/temporal once on startup so deep-links into the Temporal Web UI
// match this server's actual configuration (UI base URL + namespace).
using Microsoft.AspNetCore.Mvc;
using Temporalio.Client;

namespace MagicPAI.Server.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly ITemporalClient _temporal;
    private readonly IConfiguration _config;

    public ConfigController(ITemporalClient temporal, IConfiguration config)
    {
        _temporal = temporal;
        _config = config;
    }

    /// <summary>
    /// Returns the Temporal Web UI base URL and namespace this server is wired
    /// to. Studio uses these to build deep-links into the workflow event
    /// history. Falls back to dev defaults so a misconfigured server still
    /// produces a usable URL shape.
    /// </summary>
    [HttpGet("temporal")]
    public IActionResult GetTemporalConfig()
    {
        var uiBaseUrl = _config["Temporal:UiBaseUrl"] ?? "http://localhost:8233";
        var ns = _config["Temporal:Namespace"] ?? "default";
        return Ok(new { uiBaseUrl, @namespace = ns });
    }
}
