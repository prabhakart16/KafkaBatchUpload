namespace BulkUploadWorker.Models;

/// <summary>
/// Kafka message DTO for the worker service
/// </summary>
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

/// <summary>
/// Excel record DTO for processing
/// </summary>
public record ExcelRecordDto
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public decimal Amount { get; init; }
    public required string Date { get; init; }
}