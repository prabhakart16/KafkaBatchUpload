using Microsoft.Data.SqlClient;
using Dapper;
using System.Data;

namespace BulkUploadApi.Services;

public class BatchTrackingService : IBatchTrackingService
{
    private readonly string _connectionString;
    private readonly ILogger<BatchTrackingService> _logger;

    public BatchTrackingService(
        IConfiguration configuration,
        ILogger<BatchTrackingService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException("Connection string not found");
        _logger = logger;
    }

    public async Task<bool> IsChunkProcessedAsync(string batchId, int chunkIndex)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            const string sql = @"
                SELECT COUNT(1) 
                FROM BatchChunks 
                WHERE BatchId = @BatchId AND ChunkIndex = @ChunkIndex";

            var count = await connection.ExecuteScalarAsync<int>(sql, new { BatchId = batchId, ChunkIndex = chunkIndex });
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if chunk is processed: BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                batchId, chunkIndex);
            throw;
        }
    }

    public async Task MarkChunkReceivedAsync(string batchId, int chunkIndex, int totalChunks)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                // Upsert batch record
                const string upsertBatchSql = @"
                    MERGE INTO Batches AS target
                    USING (SELECT @BatchId AS BatchId, @TotalChunks AS TotalChunks) AS source
                    ON target.BatchId = source.BatchId
                    WHEN MATCHED THEN
                        UPDATE SET TotalChunks = source.TotalChunks
                    WHEN NOT MATCHED THEN
                        INSERT (BatchId, TotalChunks, ReceivedChunks, ProcessedChunks, Status, CreatedAt)
                        VALUES (source.BatchId, source.TotalChunks, 0, 0, 'Receiving', GETUTCDATE());";

                await connection.ExecuteAsync(upsertBatchSql,
                    new { BatchId = batchId, TotalChunks = totalChunks },
                    transaction);

                // Insert chunk tracking record
                const string insertChunkSql = @"
                    INSERT INTO BatchChunks (BatchId, ChunkIndex, ReceivedAt, Status)
                    VALUES (@BatchId, @ChunkIndex, GETUTCDATE(), 'Received')";

                await connection.ExecuteAsync(insertChunkSql,
                    new { BatchId = batchId, ChunkIndex = chunkIndex },
                    transaction);

                // Update received chunks count
                const string updateCountSql = @"
                    UPDATE Batches 
                    SET ReceivedChunks = ReceivedChunks + 1
                    WHERE BatchId = @BatchId";

                await connection.ExecuteAsync(updateCountSql,
                    new { BatchId = batchId },
                    transaction);

                await transaction.CommitAsync();

                _logger.LogInformation("Marked chunk as received: BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                    batchId, chunkIndex);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking chunk as received: BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                batchId, chunkIndex);
            throw;
        }
    }

    public async Task<BatchStatusDto?> GetBatchStatusAsync(string batchId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            const string sql = @"
                SELECT 
                    BatchId,
                    TotalChunks,
                    ReceivedChunks,
                    ProcessedChunks,
                    Status,
                    CreatedAt,
                    CompletedAt
                FROM Batches
                WHERE BatchId = @BatchId";

            var result = await connection.QueryFirstOrDefaultAsync<BatchStatusDto>(sql,
                new { BatchId = batchId });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving batch status: BatchId={BatchId}", batchId);
            throw;
        }
    }

    public async Task MarkChunkProcessedAsync(string batchId, int chunkIndex)
    {
        try
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
                    WHERE BatchId = @BatchId AND ChunkIndex = @ChunkIndex";

                await connection.ExecuteAsync(updateChunkSql,
                    new { BatchId = batchId, ChunkIndex = chunkIndex },
                    transaction);

                // Update processed chunks count
                const string updateBatchSql = @"
                    UPDATE Batches 
                    SET ProcessedChunks = ProcessedChunks + 1,
                        Status = CASE 
                            WHEN ProcessedChunks + 1 >= TotalChunks THEN 'Completed' 
                            ELSE 'Processing' 
                        END,
                        CompletedAt = CASE 
                            WHEN ProcessedChunks + 1 >= TotalChunks THEN GETUTCDATE() 
                            ELSE NULL 
                        END
                    WHERE BatchId = @BatchId";

                await connection.ExecuteAsync(updateBatchSql,
                    new { BatchId = batchId },
                    transaction);

                await transaction.CommitAsync();

                _logger.LogInformation("Marked chunk as processed: BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                    batchId, chunkIndex);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking chunk as processed: BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                batchId, chunkIndex);
            throw;
        }
    }
}