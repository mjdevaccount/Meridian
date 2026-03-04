using Microsoft.AspNetCore.Mvc;
using Meridian.Common.Models;
using Meridian.Dashboard.Models;
using Meridian.Dashboard.Services;

namespace Meridian.Dashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PositionsController : ControllerBase
{
    private readonly SqlPositionService _sqlService;
    private readonly KafkaCommandPublisher _commandPublisher;
    private readonly ILogger<PositionsController> _logger;

    public PositionsController(SqlPositionService sqlService, KafkaCommandPublisher commandPublisher, ILogger<PositionsController> logger)
    {
        _sqlService = sqlService;
        _commandPublisher = commandPublisher;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Position>>> GetAll()
    {
        var positions = await _sqlService.GetOpenPositionsAsync();
        return Ok(positions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Position>> Get(string id)
    {
        var position = await _sqlService.GetPositionAsync(id);
        if (position == null) return NotFound();
        return Ok(position);
    }

    [HttpPost]
    public async Task<ActionResult<Position>> Create([FromBody] CreatePositionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Underlier))
            return BadRequest("Underlier is required");
        if (request.Strike <= 0)
            return BadRequest("Strike must be positive");
        if (request.Expiry <= DateTime.UtcNow)
            return BadRequest("Expiry must be in the future");
        if (request.Quantity == 0)
            return BadRequest("Quantity must be non-zero");

        var positionId = $"POS-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
        var optionType = Enum.Parse<OptionType>(request.OptionType, true);
        var exerciseStyle = Enum.Parse<ExerciseStyle>(request.ExerciseStyle, true);

        var position = new Position(
            positionId,
            new Option(
                $"{request.Underlier}-{request.OptionType[0]}-{request.Strike:F0}-{(request.Expiry - DateTime.UtcNow).Days}D",
                request.Underlier.ToUpper(),
                optionType,
                request.Strike,
                request.Expiry,
                exerciseStyle),
            request.Quantity,
            request.EntryPrice,
            DateTime.UtcNow);

        await _sqlService.SavePositionAsync(position);

        var command = new PositionCommand(CommandType.Add, position, DateTime.UtcNow);
        try
        {
            await _commandPublisher.PublishAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish position command to Kafka");
        }

        _logger.LogInformation("Position {PositionId} created for {Underlier}", positionId, request.Underlier);
        return CreatedAtAction(nameof(Get), new { id = positionId }, position);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        var position = await _sqlService.GetPositionAsync(id);
        if (position == null) return NotFound();

        await _sqlService.ClosePositionAsync(id);

        var command = new PositionCommand(CommandType.Remove, position, DateTime.UtcNow);
        try
        {
            await _commandPublisher.PublishAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish remove command to Kafka");
        }

        _logger.LogInformation("Position {PositionId} removed", id);
        return NoContent();
    }
}
