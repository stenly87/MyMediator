using MediatorUseSample.Commands;
using MediatorUseSample.ExceptionHandler;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyMediator.Interfaces;

namespace MediatorUseSample.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        IMediator mediator;

        public TestController(IMediator mediator)
        {
            this.mediator = mediator;
        }

        [HttpGet("sum")]
        public async Task<ActionResult<int>> Sum(int x, int y)
        {
            return await mediator.SendAsync(new TestCalcCommand { X = x, Y = y });
        }

        [HttpGet("divide")]
        public async Task<ActionResult<double>> Devide(double x, double y)
        {
            return await mediator.SendAsync(new TestCalcDevideCommand { X = x, Y = y });
        }

        [HttpGet("test")]
        public async Task<ActionResult> Test()
        {
            await mediator.SendAsync(new TestConsoleCommand());
            return Ok();
        }

        [HttpGet("test-exception")]
        public IActionResult TestException()
        {
            throw new InvalidOperationException("Тест!");
        }
    }
}
