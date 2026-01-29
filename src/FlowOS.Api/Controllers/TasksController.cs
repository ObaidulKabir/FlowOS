using Microsoft.AspNetCore.Mvc;

namespace FlowOS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
    private readonly ILogger<TasksController> _logger;

    public TasksController(ILogger<TasksController> logger)
    {
        _logger = logger;
    }

    [HttpPost("{id}/complete")]
    public IActionResult CompleteTask(Guid id)
    {
        return Ok($"Task {id} completed");
    }
}
