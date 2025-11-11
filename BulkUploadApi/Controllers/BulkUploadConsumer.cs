using Confluent.Kafka;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Text.Json;
using Polly;
using Polly.Retry;

namespace BulkUploadWorker;

public class BulkUploadConsumer : BackgroundService
{
    private readonly ILogger<BulkUploadConsumer> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;
    private readonly IConsumer<string, string> _consumer;
    private readonly AsyncRetryPolicy _retryPolicy;

    private const int MAX_RETRY_ATTEMPTS = 3;
    private const int BATCH_INSERT_SIZE = 1000;

    public BulkUploadConsumer(
        ILogger<BulkUploadConsumer> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException("Connection string not found");

        // Configure Kafka Consumer
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = configuration["Kafka:ConsumerGroupId"] ?? "bulk-upload-consumer-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            MaxPollIntervalMs = 300000,
            SessionTimeoutMs = 45000,
            IsolationLevel = IsolationLevel.ReadCommitted,
            FetchMinBytes = 1,
            FetchWaitMaxMs = 500
        };

        _consumer = new ConsumerBuilder<string, string>(consumerConfig)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka error: {Reason}", e.Reason))
            .Build();

        // Configure retry policy with exponential backoff
        _retryPolicy = Policy
            .Handle<SqlException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                MAX_RETRY_ATTEMPTS,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Retry {RetryCount} after {Delay}s due to: {Message}",
                        retryCount, timeSpan.TotalSeconds, exception.Message);
                });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = _configuration["Kafka:Topic"] ?? "bulk-upload-topic";
        _consumer.Subscribe(topic);

        _logger.LogInformation("Kafka consumer started. Subscribed to topic: {Topic}", topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);

                    if (consumeResult?.Message != null)
                    {
                        await ProcessMessageAsync(consumeResult, stoppingToken);
                        
                        // Commit offset after successful processing
                        _consumer.Commit(consumeResult);
                        _consumer.StoreOffset(consumeResult);
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming message from Kafka");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Consumer operation cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in consumer loop");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        finally
        {
            _consumer.Close();
            _logger.LogInformation("Kafka consumer stopped");
        }
    }

    private async Task ProcessMessageAsync(
        ConsumeResult<string, string> consumeResult, 
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        KafkaMessageDto? message = null;

        try
        {
            message = JsonSerializer.Deserialize<KafkaMessageDto>(consumeResult.Message.Value);

            if (message == null)
            {
                _logger.LogError("Failed to deserialize message from offset {Offset}", 
                    consumeResult.Offset.Value);
                return;
            }

            _logger.LogInformation(
                "Processing message: MessageId={MessageId}, BatchId={BatchId}, ChunkIndex={ChunkIndex}, Records={RecordCount}",
                message.MessageId, message.BatchId, message.ChunkIndex, message.Records.Count);

            // Check idempotency - skip if already processed
            if (await IsMessageProcessedAsync(message.MessageId))
            {
                _logger.LogWarning(
                    "Message already processed (idempotent skip): MessageId={MessageId}",
                    message.MessageId);
                return;
            }

            // Process records with retry policy
            await _retryPolicy.ExecuteAsync(async () =>
            {
                await ProcessRecordsAsync(message, cancellationToken);
            });

            // Mark message as processed
            await MarkMessageProcessedAsync(message);

            // Update batch tracking
            await UpdateBatchProgressAsync(message.BatchId, message.ChunkIndex);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Successfully processed message in {Duration}ms: MessageId={MessageId}, BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                duration.TotalMilliseconds, message.MessageId, message.BatchId, message.ChunkIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process message after retries: MessageId={MessageId}, BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                message?.MessageId, message?.BatchId, message?.ChunkIndex);

            // Log to dead letter queue or error table
            if (message != null)
            {
                await LogFailedMessageAsync(message, ex.Message);
            }

            throw; // Re-throw to prevent commit
        }
    }

    private async Task ProcessRecordsAsync(KafkaMessageDto message, CancellationToken cancellationToken)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();

        try
        {
            // Batch insert for performance
            var recordBatches = message.Records
                .Select((record, index) => new { record, index })
                .GroupBy(x => x.index / BATCH_INSERT_SIZE)
                .Select(g => g.Select(x => x.record).ToList())
                .ToList();

            foreach (var batch in recordBatches)
            {
                await InsertRecordBatchAsync(connection, transaction, batch, message, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Inserted {RecordCount} records for BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                message.Records.Count, message.BatchId, message.ChunkIndex);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task InsertRecordBatchAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        List<ExcelRecordDto> records,
        KafkaMessageDto message,
        CancellationToken cancellationToken)
    {
        // Use merge for upsert (idempotency at record level)
        const string sql = @"
            MERGE INTO BulkUploadRecords AS target
            USING (
                SELECT 
                    @Id AS Id,
                    @BatchId AS BatchId,
                    @TenantId AS TenantId,
                    @Name AS Name,
                    @Email AS Email,
                    @Amount AS Amount,
                    @Date AS Date,
                    @ChunkIndex AS ChunkIndex,
                    @MessageId AS MessageId
            ) AS source
            ON target.Id = source.Id AND target.BatchId = source.BatchId
            WHEN MATCHED THEN
                UPDATE SET 
                    Name = source.Name,
                    Email = source.Email,
                    Amount = source.Amount,
                    Date = source.Date,
                    UpdatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (Id, BatchId, TenantId, Name, Email, Amount, Date, ChunkIndex, MessageId, CreatedAt)
                VALUES (source.Id, source.BatchId, source.TenantId, source.Name, source.Email, 
                        source.Amount, source.Date, source.ChunkIndex, source.MessageId, GETUTCDATE());";

        var parameters = records.Select(record => new
        {
            Id = record.Id,
            BatchId = message.BatchId,
            TenantId = record.TenantId,
            Name = record.Name,
            Email = record.Email,
            Amount = record.Amount,
            Date = DateTime.TryParse(record.Date, out var date) ? date : DateTime.UtcNow,
            ChunkIndex = message.ChunkIndex,
            MessageId = message.MessageId
        }).ToList();

        var rowsAffected = await connection.ExecuteAsync(sql, parameters, transaction);

        _logger.LogDebug(
            "Batch insert affected {RowsAffected} rows for {RecordCount} records",
            rowsAffected, records.Count);
    }

    private async Task<bool> IsMessageProcessedAsync(string messageId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT COUNT(1) 
            FROM ProcessedMessages 
            WHERE MessageId = @MessageId";

        var count = await connection.ExecuteScalarAsync<int>(sql, new { MessageId = messageId });
        return count > 0;
    }

    private async Task MarkMessageProcessedAsync(KafkaMessageDto message)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            INSERT INTO ProcessedMessages (MessageId, BatchId, ChunkIndex, ProcessedAt)
            VALUES (@MessageId, @BatchId, @ChunkIndex, GETUTCDATE())";

        await connection.ExecuteAsync(sql, new
        {
            MessageId = message.MessageId,
            BatchId = message.BatchId,
            ChunkIndex = message.ChunkIndex
        });
    }

    private async Task UpdateBatchProgressAsync(string batchId, int chunkIndex)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        try
        {
            // Update chunk status
            const string updateChunkSql = @"
                UPDATE BatchChunks 
                SET Status = 'Processed', ProcessedAt = GETUTCDATE()
                WHERE BatchId = @BatchId AND ChunkIndex = @ChunkIndex AND Status != 'Processed'";

            await connection.ExecuteAsync(updateChunkSql, 
                new { BatchId = batchId, ChunkIndex = chunkIndex }, 
                transaction);

            // Update batch progress
            const string updateBatchSql = @"
                UPDATE Batches 
                SET ProcessedChunks = (
                        SELECT COUNT(*) 
                        FROM BatchChunks 
                        WHERE BatchId = @BatchId AND Status = 'Processed'
                    ),
                    Status = CASE 
                        WHEN (SELECT COUNT(*) FROM BatchChunks WHERE BatchId = @BatchId AND Status = 'Processed') 
                             >= TotalChunks 
                        THEN 'Completed' 
                        ELSE 'Processing' 
                    END,
                    CompletedAt = CASE 
                        WHEN (SELECT COUNT(*) FROM BatchChunks WHERE BatchId = @BatchId AND Status = 'Processed') 
                             >= TotalChunks 
                        THEN GETUTCDATE() 
                        ELSE NULL 
                    END
                WHERE BatchId = @BatchId";

            await connection.ExecuteAsync(updateBatchSql, 
                new { BatchId = batchId }, 
                transaction);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task LogFailedMessageAsync(KafkaMessageDto message, string errorMessage)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            const string sql = @"
                INSERT INTO FailedMessages (MessageId, BatchId, ChunkIndex, ErrorMessage, FailedAt, Payload)
                VALUES (@MessageId, @BatchId, @ChunkIndex, @ErrorMessage, GETUTCDATE(), @Payload)";

            await connection.ExecuteAsync(sql, new
            {
                MessageId = message.MessageId,
                BatchId = message.BatchId,
                ChunkIndex = message.ChunkIndex,
                ErrorMessage = errorMessage,
                Payload = JsonSerializer.Serialize(message)
            });

            _logger.LogInformation("Logged failed message to database: MessageId={MessageId}", message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log failed message: MessageId={MessageId}", message.MessageId);
        }
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}

// DTOs (matching API)
public record KafkaMessageDto
{
    public required string MessageId { get; init; }
    public required string BatchId { get; init; }
    public required int ChunkIndex { get; init; }
    public required int TotalChunks { get; init; }
    public int SubChunkIndex { get; init; }
    public int TotalSubChunks { get; init; }
    public string? TenantId { get; init; }
    public DateTime Timestamp { get; init; }
    public required List<ExcelRecordDto> Records { get; init; }
}

public record ExcelRecordDto
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public decimal Amount { get; init; }
    public required string Date { get; init; }
}