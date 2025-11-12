namespace BulkUploadApi.Models;/// <summary>
/// Payload received from Angular UI containing a chunk of records
/// </summary>
public record ChunkPayloadDto
{
    public required string BatchId { get; init; }
    public required int ChunkIndex { get; init; }
    public required int TotalChunks { get; init; }
    public string? TenantId { get; init; }
    public required List<ExcelRecordDto> Records { get; init; }
}

/// <summary>
/// Individual Excel record data
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

/// <summary>
/// Kafka message wrapper with metadata
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
/// API response sent back to Angular UI
/// </summary>
public record ApiResponseDto
{
    public bool Success { get; init; }
    public required string Message { get; init; }
    public required string BatchId { get; init; }
    public int ChunkIndex { get; init; }
}