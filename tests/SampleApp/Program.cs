using Messaging.OutboxInbox.AspNetCore.Extensions;
using Messaging.OutboxInbox.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using SampleApp.Data;
using SampleApp.Messages;
using SampleApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.IncludeOutboxMessaging();
    options.IncludeInboxMessaging();
});

builder.AddOutboxAndInboxMessaging<AppDbContext>(config =>
{
    config.AddSubscriber<OrderCreatedMessage, OrderCreatedMessageHandler>();
});

builder.Services.AddScoped<OrderService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.MigrateAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.MapPost("/orders", async (OrderService service, CreateOrderDto dto) =>
{
    var orderId = await service.CreateOrderAsync(dto.CustomerName, dto.TotalAmount);
    return Results.Ok(new { orderId });
});

app.Run();

public record CreateOrderDto(string CustomerName, decimal TotalAmount);