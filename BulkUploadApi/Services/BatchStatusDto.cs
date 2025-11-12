


namespace BulkUploadApi.Models;

/// <summary>
/// Data transfer object representing the status of a batch upload
/// </summary>
public record BatchStatusDto
{
    /// <summary>
    /// Unique identifier for the batch
    /// </summary>
    public required string BatchId { get; init; }

    /// <summary>
    /// Total number of chunks expected in this batch
    /// </summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// Number of chunks received by the API
    /// </summary>
    public int ReceivedChunks { get; init; }

    /// <summary>
    /// Number of chunks fully processed by workers
    /// </summary>
    public int ProcessedChunks { get; init; }

    /// <summary>
    /// Timestamp when the batch was created
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when the batch was fully completed (null if still processing)
    /// </summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>
    /// Current status: Receiving, Processing, Completed, Failed
    /// </summary>
    public string Status { get; init; } = "Processing";

    /// <summary>
    /// Percentage of completion (0-100)
    /// </summary>
    public decimal PercentComplete => TotalChunks > 0
        ? Math.Round((decimal)ProcessedChunks / TotalChunks * 100, 2)
        : 0;

    /// <summary>
    /// Duration of processing in seconds (null if not completed)
    /// </summary>
    public int? DurationSeconds => CompletedAt.HasValue
        ? (int)(CompletedAt.Value - CreatedAt).TotalSeconds
        : null;
}