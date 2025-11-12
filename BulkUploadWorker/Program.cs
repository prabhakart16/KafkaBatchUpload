using BulkUploadWorker;

var builder = Host.CreateApplicationBuilder(args);

// Add worker service
builder.Services.AddHostedService<BulkUploadConsumer>();

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Configure connection string from appsettings
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

var host = builder.Build();

host.Run();