using BulkUploadApi.Services;
using BulkUploadApi.Models;     // Add this
using Confluent.Kafka;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configure Kafka Producer
builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var config = new ProducerConfig
    {
        BootstrapServers = builder.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
        Acks = Acks.All,
        EnableIdempotence = true,
        MaxInFlight = 5,
        MessageSendMaxRetries = 10,
        CompressionType = CompressionType.Snappy,
        LingerMs = 10,
        BatchSize = 1000000,
        MessageMaxBytes = 1000000,
        RequestTimeoutMs = 30000,
        ClientId = "bulk-upload-api"
    };

    return new ProducerBuilder<string, string>(config).Build();
});

// Register services
builder.Services.AddScoped<IBatchTrackingService, BatchTrackingService>();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAngular");
app.UseAuthorization();
app.MapControllers();

app.Run();