using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using MyMediator.Interfaces;
using MyMediator.Types;
// ─── Запросы ───

public record PingRequest(string Message) : IRequest<PongResponse>;
public record PongResponse(string Message);

public record VoidCommand(string Data) : IRequest;

// ─── Хэндлеры ───

public class PingHandler : IRequestHandler<PingRequest, PongResponse>
{
    public Task<PongResponse> HandleAsync(PingRequest request, CancellationToken ct = default)
        => Task.FromResult(new PongResponse($"Pong: {request.Message}"));
}

public class VoidHandler : IRequestHandler<VoidCommand, Unit>
{
    public Task<Unit> HandleAsync(VoidCommand request, CancellationToken ct = default)
        => Task.FromResult(Unit.Value);
}

// ─── Поведения ───

public class NoOpBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
        => await next();
}

// ─── Бенчмарки ───

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class MediatorBenchmarks
{
    private IMediator _mediator = null!;
    private PingRequest _pingRequest = null!;
    private VoidCommand _voidCommand = null!;
    private IServiceProvider _serviceProvider = null!;

    // Прямой вызов handler'а — baseline для сравнения
    private IRequestHandler<PingRequest, PongResponse> _handlerDirect = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        // Регистрация медиатора
        services.AddSingleton<IMediator, Mediator>();

        // Регистрация хэндлеров
        services.AddTransient<IRequestHandler<PingRequest, PongResponse>, PingHandler>();
        services.AddTransient<IRequestHandler<VoidCommand, Unit>, VoidHandler>();

        // Регистрация поведений (0, 1, 3 — через отдельные конфигурации)
        // Для этого бенчмарка фиксируем 3 поведения
        services.AddTransient<IPipelineBehavior<PingRequest, PongResponse>, NoOpBehavior<PingRequest, PongResponse>>();
        services.AddTransient<IPipelineBehavior<PingRequest, PongResponse>, NoOpBehavior<PingRequest, PongResponse>>();
        services.AddTransient<IPipelineBehavior<PingRequest, PongResponse>, NoOpBehavior<PingRequest, PongResponse>>();

        services.AddTransient<IPipelineBehavior<VoidCommand, Unit>, NoOpBehavior<VoidCommand, Unit>>();
        services.AddTransient<IPipelineBehavior<VoidCommand, Unit>, NoOpBehavior<VoidCommand, Unit>>();
        services.AddTransient<IPipelineBehavior<VoidCommand, Unit>, NoOpBehavior<VoidCommand, Unit>>();

        _serviceProvider = services.BuildServiceProvider();
        _mediator = _serviceProvider.GetRequiredService<IMediator>();
        _handlerDirect = _serviceProvider.GetRequiredService<IRequestHandler<PingRequest, PongResponse>>();

        _pingRequest = new PingRequest("Hello");
        _voidCommand = new VoidCommand("Data");

        // Прогрев JIT: первый вызов компилирует Expression Trees
        _mediator.SendAsync(_pingRequest).GetAwaiter().GetResult();
        _mediator.SendAsync(_voidCommand).GetAwaiter().GetResult();
    }

    // ── Baseline: прямой вызов handler'а без медиатора ──

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("WithResponse")]
    public Task<PongResponse> Direct_Handler_Call()
        => _handlerDirect.HandleAsync(_pingRequest);

    // ── Mediator: IRequest<TResponse> ──

    [Benchmark]
    [BenchmarkCategory("WithResponse")]
    public Task<PongResponse> Mediator_SendAsync_WithResponse()
        => _mediator.SendAsync(_pingRequest);

    // ── Mediator: IRequest (void → Unit) ──

    [Benchmark]
    [BenchmarkCategory("Void")]
    public Task Mediator_SendAsync_Void()
        => _mediator.SendAsync(_voidCommand);

    // ── Синхронный вызов для проверки overhead Task ──

    [Benchmark]
    [BenchmarkCategory("WithResponse")]
    public PongResponse Mediator_SendAsync_WithResponse_Sync()
        => _mediator.SendAsync(_pingRequest).GetAwaiter().GetResult();
}

// ─── Запуск ───

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<MediatorBenchmarks>();
    }
}