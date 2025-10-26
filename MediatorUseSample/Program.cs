using MediatorUseSample.Behaviors;
using MediatorUseSample.Commands;
using MediatorUseSample.Commands.ValidateCommand;
using MediatorUseSample.ExceptionHandler;
using MyMediator.Extension;
using MyMediator.Interfaces;
using MyMediator.Types;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IMediator, Mediator>();

// 3! команда запустится после поведений ниже
builder.Services.AddMediatorHandlers(Assembly.GetExecutingAssembly());

// 2! потом для команды запустится ValidatorBehavior
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidatorBehavior<,>));

// 1! сначала для команды запустится StopWatchBehavior
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(StopWatchBehavior<,>));

// добавление валидатора для команды
builder.Services.AddTransient<IValidator<TestCalcCommand>, TestCalcCommandValidator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// глобальный обработчик ошибок
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
