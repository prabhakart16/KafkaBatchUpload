
using BulkUploadApi.Models;

namespace BulkUploadApi.Services;

/// <summary>
/// Service interface for tracking batch upload progress and status
/// </summary>
public interface IBatchTrackingService
{
    /// <summary>
    /// Checks if a chunk has already been processed (for idempotency)
    /// </summary>
    /// <param name="batchId">The batch identifier</param>
    /// <param name="chunkIndex">The chunk index within the batch</param>
    /// <returns>True if the chunk has been processed, false otherwise</returns>
    Task<bool> IsChunkProcessedAsync(string batchId, int chunkIndex);

    /// <summary>
    /// Marks a chunk as received in the tracking system
    /// </summary>
    /// <param name="batchId">The batch identifier</param>
    /// <param name="chunkIndex">The chunk index within the batch</param>
    /// <param name="totalChunks">Total number of chunks in the batch</param>
    Task MarkChunkReceivedAsync(string batchId, int chunkIndex, int totalChunks);

    /// <summary>
    /// Retrieves the current status of a batch
    /// </summary>
    /// <param name="batchId">The batch identifier</param>
    /// <returns>Batch status details or null if not found</returns>
    Task<BatchStatusDto?> GetBatchStatusAsync(string batchId);

    /// <summary>
    /// Marks a chunk as fully processed
    /// </summary>
    /// <param name="batchId">The batch identifier</param>
    /// <param name="chunkIndex">The chunk index within the batch</param>
    Task MarkChunkProcessedAsync(string batchId, int chunkIndex);
}


