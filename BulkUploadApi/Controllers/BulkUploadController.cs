using BulkUploadApi.Models;
using BulkUploadApi.Services;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BulkUploadApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BulkUploadController : ControllerBase
{
    private readonly IProducer<string, string> _kafkaProducer;
    private readonly ILogger<BulkUploadController> _logger;
    private readonly IBatchTrackingService _batchTrackingService;
    private readonly IConfiguration _configuration;

    private const int MAX_KAFKA_MESSAGE_SIZE = 900_000; // ~900KB to stay under 1MB limit
    private const string KAFKA_TOPIC = "bulk-upload-topic";

    public BulkUploadController(
        IProducer<string, string> kafkaProducer,
        ILogger<BulkUploadController> logger,
        IBatchTrackingService batchTrackingService,
        IConfiguration configuration)
    {
        _kafkaProducer = kafkaProducer;
        _logger = logger;
        _batchTrackingService = batchTrackingService;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> UploadChunk([FromBody] ChunkPayloadDto payload)
    {
        try
        {
            // Enhanced validation
            if (payload == null)
            {
                _logger.LogWarning("Received null payload");
                return BadRequest(new ApiResponseDto
                {
                    Success = false,
                    Message = "Payload is required",
                    BatchId = string.Empty,
                    ChunkIndex = -1
                });
            }

            if (string.IsNullOrWhiteSpace(payload.BatchId))
            {
                return BadRequest(new ApiResponseDto
                {
                    Success = false,
                    Message = "BatchId is required",
                    BatchId = string.Empty,
                    ChunkIndex = payload.ChunkIndex
                });
            }

            if (payload.Records == null || !payload.Records.Any())
            {
                return BadRequest(new ApiResponseDto
                {
                    Success = false,
                    Message = "Records array is required and must contain at least one record",
                    BatchId = payload.BatchId,
                    ChunkIndex = payload.ChunkIndex
                });
            }

            // Validate individual records
            for (int i = 0; i < payload.Records.Count; i++)
            {
                var record = payload.Records[i];
                if (string.IsNullOrWhiteSpace(record.Id))
                {
                    return BadRequest(new ApiResponseDto
                    {
                        Success = false,
                        Message = $"Record at index {i} has invalid Id",
                        BatchId = payload.BatchId,
                        ChunkIndex = payload.ChunkIndex
                    });
                }

                if (string.IsNullOrWhiteSpace(record.TenantId))
                {
                    return BadRequest(new ApiResponseDto
                    {
                        Success = false,
                        Message = $"Record at index {i} has invalid TenantId",
                        BatchId = payload.BatchId,
                        ChunkIndex = payload.ChunkIndex
                    });
                }

                // Validate date format
                if (!DateTime.TryParse(record.Date, out _))
                {
                    return BadRequest(new ApiResponseDto
                    {
                        Success = false,
                        Message = $"Record at index {i} has invalid Date format. Expected: yyyy-MM-dd or ISO 8601",
                        BatchId = payload.BatchId,
                        ChunkIndex = payload.ChunkIndex
                    });
                }
            }

            _logger.LogInformation(
                "Received chunk {ChunkIndex}/{TotalChunks} for batch {BatchId} with {RecordCount} records",
                payload.ChunkIndex, payload.TotalChunks, payload.BatchId, payload.Records.Count);

            // Check for duplicate chunk (idempotency)
            var isDuplicate = await _batchTrackingService.IsChunkProcessedAsync(
                payload.BatchId, payload.ChunkIndex);

            if (isDuplicate)
            {
                _logger.LogWarning(
                    "Duplicate chunk detected: BatchId={BatchId}, ChunkIndex={ChunkIndex}",
                    payload.BatchId, payload.ChunkIndex);

                return Ok(new ApiResponseDto
                {
                    Success = true,
                    Message = "Chunk already processed (duplicate)",
                    BatchId = payload.BatchId,
                    ChunkIndex = payload.ChunkIndex
                });
            }

            // Mark chunk as received
            await _batchTrackingService.MarkChunkReceivedAsync(
                payload.BatchId, payload.ChunkIndex, payload.TotalChunks);

            // Publish to Kafka (split if needed)
            await PublishToKafkaAsync(payload);

            return Ok(new ApiResponseDto
            {
                Success = true,
                Message = "Chunk received and queued for processing",
                BatchId = payload.BatchId,
                ChunkIndex = payload.ChunkIndex
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing chunk {ChunkIndex} for batch {BatchId}",
                payload?.ChunkIndex, payload?.BatchId);

            return StatusCode(500, new ApiResponseDto
            {
                Success = false,
                Message = $"Internal server error: {ex.Message}",
                BatchId = payload?.BatchId ?? string.Empty,
                ChunkIndex = payload?.ChunkIndex ?? -1
            });
        }
    }

    [HttpGet("status/{batchId}")]
    public async Task<IActionResult> GetBatchStatus(string batchId)
    {
        try
        {
            var status = await _batchTrackingService.GetBatchStatusAsync(batchId);

            if (status == null)
            {
                return NotFound(new { Message = "Batch not found" });
            }

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving batch status for {BatchId}", batchId);
            return StatusCode(500, new { Message = "Error retrieving batch status" });
        }
    }

    private async Task PublishToKafkaAsync(ChunkPayloadDto payload)
    {
        // Generate partition key (stable hash for ordered processing)
        var partitionKey = GeneratePartitionKey(payload.BatchId, payload.TenantId);

        // Create Kafka message wrapper
        var kafkaMessage = new KafkaMessageDto
        {
            MessageId = Guid.NewGuid().ToString(),
            BatchId = payload.BatchId,
            ChunkIndex = payload.ChunkIndex,
            TotalChunks = payload.TotalChunks,
            TenantId = payload.TenantId,
            Timestamp = DateTime.UtcNow,
            Records = payload.Records
        };

        var serializedMessage = JsonSerializer.Serialize(kafkaMessage);
        var messageSize = Encoding.UTF8.GetByteCount(serializedMessage);

        // If message exceeds Kafka limit, split into sub-chunks
        if (messageSize > MAX_KAFKA_MESSAGE_SIZE)
        {
            await PublishLargeMessageAsync(kafkaMessage, partitionKey);
        }
        else
        {
            await PublishSingleMessageAsync(partitionKey, serializedMessage, kafkaMessage);
        }
    }

    private async Task PublishSingleMessageAsync(
        string partitionKey,
        string serializedMessage,
        KafkaMessageDto kafkaMessage)
    {
        var message = new Message<string, string>
        {
            Key = partitionKey,
            Value = serializedMessage,
            Headers = new Headers
            {
                { "BatchId", Encoding.UTF8.GetBytes(kafkaMessage.BatchId) },
                { "ChunkIndex", Encoding.UTF8.GetBytes(kafkaMessage.ChunkIndex.ToString()) },
                { "MessageId", Encoding.UTF8.GetBytes(kafkaMessage.MessageId) }
            }
        };

        var deliveryResult = await _kafkaProducer.ProduceAsync(KAFKA_TOPIC, message);

        _logger.LogInformation(
            "Published message to Kafka: Topic={Topic}, Partition={Partition}, Offset={Offset}, BatchId={BatchId}",
            deliveryResult.Topic, deliveryResult.Partition.Value,
            deliveryResult.Offset.Value, kafkaMessage.BatchId);
    }

    private async Task PublishLargeMessageAsync(KafkaMessageDto kafkaMessage, string partitionKey)
    {
        // Split records into smaller batches
        const int subChunkSize = 1000;
        var subChunks = kafkaMessage.Records
            .Select((record, index) => new { record, index })
            .GroupBy(x => x.index / subChunkSize)
            .Select(g => g.Select(x => x.record).ToList())
            .ToList();

        _logger.LogInformation(
            "Splitting large message into {SubChunkCount} sub-chunks for BatchId={BatchId}, ChunkIndex={ChunkIndex}",
            subChunks.Count, kafkaMessage.BatchId, kafkaMessage.ChunkIndex);

        for (int i = 0; i < subChunks.Count; i++)
        {
            var subMessage = new KafkaMessageDto
            {
                MessageId = $"{kafkaMessage.MessageId}_sub_{i}",
                BatchId = kafkaMessage.BatchId,
                ChunkIndex = kafkaMessage.ChunkIndex,
                TotalChunks = kafkaMessage.TotalChunks,
                SubChunkIndex = i,
                TotalSubChunks = subChunks.Count,
                TenantId = kafkaMessage.TenantId,
                Timestamp = kafkaMessage.Timestamp,
                Records = subChunks[i]
            };

            var serialized = JsonSerializer.Serialize(subMessage);
            await PublishSingleMessageAsync(partitionKey, serialized, subMessage);
        }
    }

    private string GeneratePartitionKey(string batchId, string? tenantId)
    {
        // Use tenantId for partition key to ensure all records for a tenant 
        // go to the same partition (ordered processing)
        var key = !string.IsNullOrEmpty(tenantId) ? tenantId : batchId;

        // Hash to ensure even distribution across partitions
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hashBytes);
    }
}