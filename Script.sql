USE [master]
GO
/****** Object:  Database [BulkUploadDB]    Script Date: 11-11-2025 19:44:59 ******/
CREATE DATABASE [BulkUploadDB]
 CONTAINMENT = NONE
 ON  PRIMARY 
( NAME = N'BulkUploadDB', FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\BulkUploadDB.mdf' , SIZE = 8192KB , MAXSIZE = UNLIMITED, FILEGROWTH = 65536KB )
 LOG ON 
( NAME = N'BulkUploadDB_log', FILENAME = N'C:\Program Files\Microsoft SQL Server\MSSQL15.MSSQLSERVER\MSSQL\DATA\BulkUploadDB_log.ldf' , SIZE = 8192KB , MAXSIZE = 2048GB , FILEGROWTH = 65536KB )
 WITH CATALOG_COLLATION = DATABASE_DEFAULT
GO
ALTER DATABASE [BulkUploadDB] SET COMPATIBILITY_LEVEL = 150
GO
IF (1 = FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'))
begin
EXEC [BulkUploadDB].[dbo].[sp_fulltext_database] @action = 'enable'
end
GO
ALTER DATABASE [BulkUploadDB] SET ANSI_NULL_DEFAULT OFF 
GO
ALTER DATABASE [BulkUploadDB] SET ANSI_NULLS OFF 
GO
ALTER DATABASE [BulkUploadDB] SET ANSI_PADDING OFF 
GO
ALTER DATABASE [BulkUploadDB] SET ANSI_WARNINGS OFF 
GO
ALTER DATABASE [BulkUploadDB] SET ARITHABORT OFF 
GO
ALTER DATABASE [BulkUploadDB] SET AUTO_CLOSE OFF 
GO
ALTER DATABASE [BulkUploadDB] SET AUTO_SHRINK OFF 
GO
ALTER DATABASE [BulkUploadDB] SET AUTO_UPDATE_STATISTICS ON 
GO
ALTER DATABASE [BulkUploadDB] SET CURSOR_CLOSE_ON_COMMIT OFF 
GO
ALTER DATABASE [BulkUploadDB] SET CURSOR_DEFAULT  GLOBAL 
GO
ALTER DATABASE [BulkUploadDB] SET CONCAT_NULL_YIELDS_NULL OFF 
GO
ALTER DATABASE [BulkUploadDB] SET NUMERIC_ROUNDABORT OFF 
GO
ALTER DATABASE [BulkUploadDB] SET QUOTED_IDENTIFIER OFF 
GO
ALTER DATABASE [BulkUploadDB] SET RECURSIVE_TRIGGERS OFF 
GO
ALTER DATABASE [BulkUploadDB] SET  DISABLE_BROKER 
GO
ALTER DATABASE [BulkUploadDB] SET AUTO_UPDATE_STATISTICS_ASYNC OFF 
GO
ALTER DATABASE [BulkUploadDB] SET DATE_CORRELATION_OPTIMIZATION OFF 
GO
ALTER DATABASE [BulkUploadDB] SET TRUSTWORTHY OFF 
GO
ALTER DATABASE [BulkUploadDB] SET ALLOW_SNAPSHOT_ISOLATION OFF 
GO
ALTER DATABASE [BulkUploadDB] SET PARAMETERIZATION SIMPLE 
GO
ALTER DATABASE [BulkUploadDB] SET READ_COMMITTED_SNAPSHOT OFF 
GO
ALTER DATABASE [BulkUploadDB] SET HONOR_BROKER_PRIORITY OFF 
GO
ALTER DATABASE [BulkUploadDB] SET RECOVERY FULL 
GO
ALTER DATABASE [BulkUploadDB] SET  MULTI_USER 
GO
ALTER DATABASE [BulkUploadDB] SET PAGE_VERIFY CHECKSUM  
GO
ALTER DATABASE [BulkUploadDB] SET DB_CHAINING OFF 
GO
ALTER DATABASE [BulkUploadDB] SET FILESTREAM( NON_TRANSACTED_ACCESS = OFF ) 
GO
ALTER DATABASE [BulkUploadDB] SET TARGET_RECOVERY_TIME = 60 SECONDS 
GO
ALTER DATABASE [BulkUploadDB] SET DELAYED_DURABILITY = DISABLED 
GO
ALTER DATABASE [BulkUploadDB] SET ACCELERATED_DATABASE_RECOVERY = OFF  
GO
EXEC sys.sp_db_vardecimal_storage_format N'BulkUploadDB', N'ON'
GO
ALTER DATABASE [BulkUploadDB] SET QUERY_STORE = OFF
GO
USE [BulkUploadDB]
GO
/****** Object:  Table [dbo].[Batches]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Batches](
	[BatchId] [varchar](100) NOT NULL,
	[TotalChunks] [int] NOT NULL,
	[ReceivedChunks] [int] NOT NULL,
	[ProcessedChunks] [int] NOT NULL,
	[Status] [varchar](50) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[CompletedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[BatchId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[BulkUploadRecords]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[BulkUploadRecords](
	[RecordId] [bigint] IDENTITY(1,1) NOT NULL,
	[Id] [varchar](100) NOT NULL,
	[BatchId] [varchar](100) NOT NULL,
	[TenantId] [varchar](100) NOT NULL,
	[Name] [nvarchar](255) NOT NULL,
	[Email] [nvarchar](255) NOT NULL,
	[Amount] [decimal](18, 2) NOT NULL,
	[Date] [datetime2](7) NOT NULL,
	[CreatedAt] [datetime2](7) NOT NULL,
	[UpdatedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[RecordId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_BulkUploadRecords_Id_BatchId] UNIQUE NONCLUSTERED 
(
	[Id] ASC,
	[BatchId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  View [dbo].[vw_ActiveBatches]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE VIEW [dbo].[vw_ActiveBatches] AS
SELECT 
    b.BatchId,
    b.TotalChunks,
    b.ReceivedChunks,
    b.ProcessedChunks,
    b.Status,
    b.CreatedAt,
    DATEDIFF(MINUTE, b.CreatedAt, GETUTCDATE()) AS AgeMinutes,
    CAST(b.ProcessedChunks AS FLOAT) / NULLIF(b.TotalChunks, 0) * 100 AS PercentComplete,
    COUNT(DISTINCT r.RecordId) AS RecordCount
FROM Batches b
LEFT JOIN BulkUploadRecords r ON b.BatchId = r.BatchId
WHERE b.Status IN ('Receiving', 'Processing')
GROUP BY 
    b.BatchId, b.TotalChunks, b.ReceivedChunks, b.ProcessedChunks, 
    b.Status, b.CreatedAt;
GO
/****** Object:  View [dbo].[vw_ProcessingStats]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE VIEW [dbo].[vw_ProcessingStats] AS
SELECT 
    CAST(b.CreatedAt AS DATE) AS ProcessingDate,
    COUNT(DISTINCT b.BatchId) AS TotalBatches,
    SUM(b.TotalChunks) AS TotalChunks,
    SUM(b.ProcessedChunks) AS ProcessedChunks,
    COUNT(DISTINCT r.RecordId) AS TotalRecords,
    AVG(DATEDIFF(SECOND, b.CreatedAt, b.CompletedAt)) AS AvgProcessingTimeSeconds
FROM Batches b
LEFT JOIN BulkUploadRecords r ON b.BatchId = r.BatchId
WHERE b.Status = 'Completed'
GROUP BY CAST(b.CreatedAt AS DATE);
GO
/****** Object:  Table [dbo].[BatchChunks]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[BatchChunks](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[BatchId] [varchar](100) NOT NULL,
	[ChunkIndex] [int] NOT NULL,
	[Status] [varchar](50) NOT NULL,
	[ReceivedAt] [datetime2](7) NOT NULL,
	[ProcessedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
 CONSTRAINT [UQ_BatchChunks_BatchId_ChunkIndex] UNIQUE NONCLUSTERED 
(
	[BatchId] ASC,
	[ChunkIndex] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
/****** Object:  Table [dbo].[FailedMessages]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[FailedMessages](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[MessageId] [varchar](100) NOT NULL,
	[BatchId] [varchar](100) NOT NULL,
	[ChunkIndex] [int] NOT NULL,
	[ErrorMessage] [nvarchar](max) NOT NULL,
	[FailedAt] [datetime2](7) NOT NULL,
	[Payload] [nvarchar](max) NULL,
	[Retried] [bit] NOT NULL,
	[RetriedAt] [datetime2](7) NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO
/****** Object:  Table [dbo].[ProcessedMessages]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[ProcessedMessages](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[MessageId] [varchar](100) NOT NULL,
	[BatchId] [varchar](100) NOT NULL,
	[ChunkIndex] [int] NOT NULL,
	[ProcessedAt] [datetime2](7) NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY],
UNIQUE NONCLUSTERED 
(
	[MessageId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_BatchChunks_BatchId]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_BatchChunks_BatchId] ON [dbo].[BatchChunks]
(
	[BatchId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_BatchChunks_Status]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_BatchChunks_Status] ON [dbo].[BatchChunks]
(
	[Status] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_Batches_CreatedAt]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_Batches_CreatedAt] ON [dbo].[Batches]
(
	[CreatedAt] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_Batches_Status]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_Batches_Status] ON [dbo].[Batches]
(
	[Status] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_BulkUploadRecords_BatchId]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_BulkUploadRecords_BatchId] ON [dbo].[BulkUploadRecords]
(
	[BatchId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_BulkUploadRecords_CreatedAt]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_BulkUploadRecords_CreatedAt] ON [dbo].[BulkUploadRecords]
(
	[CreatedAt] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_BulkUploadRecords_Email]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_BulkUploadRecords_Email] ON [dbo].[BulkUploadRecords]
(
	[Email] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_BulkUploadRecords_TenantId]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_BulkUploadRecords_TenantId] ON [dbo].[BulkUploadRecords]
(
	[TenantId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_FailedMessages_BatchId]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_FailedMessages_BatchId] ON [dbo].[FailedMessages]
(
	[BatchId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_FailedMessages_FailedAt]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_FailedMessages_FailedAt] ON [dbo].[FailedMessages]
(
	[FailedAt] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_FailedMessages_Retried]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_FailedMessages_Retried] ON [dbo].[FailedMessages]
(
	[Retried] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_ProcessedMessages_BatchId]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_ProcessedMessages_BatchId] ON [dbo].[ProcessedMessages]
(
	[BatchId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
SET ANSI_PADDING ON
GO
/****** Object:  Index [IX_ProcessedMessages_MessageId]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_ProcessedMessages_MessageId] ON [dbo].[ProcessedMessages]
(
	[MessageId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
/****** Object:  Index [IX_ProcessedMessages_ProcessedAt]    Script Date: 11-11-2025 19:45:00 ******/
CREATE NONCLUSTERED INDEX [IX_ProcessedMessages_ProcessedAt] ON [dbo].[ProcessedMessages]
(
	[ProcessedAt] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
GO
ALTER TABLE [dbo].[BatchChunks] ADD  DEFAULT ('Received') FOR [Status]
GO
ALTER TABLE [dbo].[BatchChunks] ADD  DEFAULT (getutcdate()) FOR [ReceivedAt]
GO
ALTER TABLE [dbo].[Batches] ADD  DEFAULT ((0)) FOR [ReceivedChunks]
GO
ALTER TABLE [dbo].[Batches] ADD  DEFAULT ((0)) FOR [ProcessedChunks]
GO
ALTER TABLE [dbo].[Batches] ADD  DEFAULT ('Receiving') FOR [Status]
GO
ALTER TABLE [dbo].[Batches] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[BulkUploadRecords] ADD  DEFAULT (getutcdate()) FOR [CreatedAt]
GO
ALTER TABLE [dbo].[FailedMessages] ADD  DEFAULT (getutcdate()) FOR [FailedAt]
GO
ALTER TABLE [dbo].[FailedMessages] ADD  DEFAULT ((0)) FOR [Retried]
GO
ALTER TABLE [dbo].[ProcessedMessages] ADD  DEFAULT (getutcdate()) FOR [ProcessedAt]
GO
ALTER TABLE [dbo].[BatchChunks]  WITH CHECK ADD  CONSTRAINT [FK_BatchChunks_Batches] FOREIGN KEY([BatchId])
REFERENCES [dbo].[Batches] ([BatchId])
GO
ALTER TABLE [dbo].[BatchChunks] CHECK CONSTRAINT [FK_BatchChunks_Batches]
GO
ALTER TABLE [dbo].[BulkUploadRecords]  WITH CHECK ADD  CONSTRAINT [FK_BulkUploadRecords_Batches] FOREIGN KEY([BatchId])
REFERENCES [dbo].[Batches] ([BatchId])
GO
ALTER TABLE [dbo].[BulkUploadRecords] CHECK CONSTRAINT [FK_BulkUploadRecords_Batches]
GO
ALTER TABLE [dbo].[ProcessedMessages]  WITH CHECK ADD  CONSTRAINT [FK_ProcessedMessages_Batches] FOREIGN KEY([BatchId])
REFERENCES [dbo].[Batches] ([BatchId])
GO
ALTER TABLE [dbo].[ProcessedMessages] CHECK CONSTRAINT [FK_ProcessedMessages_Batches]
GO
ALTER TABLE [dbo].[BatchChunks]  WITH CHECK ADD  CONSTRAINT [CHK_BatchChunks_Status] CHECK  (([Status]='Failed' OR [Status]='Processed' OR [Status]='Processing' OR [Status]='Received'))
GO
ALTER TABLE [dbo].[BatchChunks] CHECK CONSTRAINT [CHK_BatchChunks_Status]
GO
ALTER TABLE [dbo].[Batches]  WITH CHECK ADD  CONSTRAINT [CHK_Batches_Status] CHECK  (([Status]='Failed' OR [Status]='Completed' OR [Status]='Processing' OR [Status]='Receiving'))
GO
ALTER TABLE [dbo].[Batches] CHECK CONSTRAINT [CHK_Batches_Status]
GO
/****** Object:  StoredProcedure [dbo].[sp_CleanupProcessedMessages]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Cleanup old processed messages (for maintenance)
CREATE PROCEDURE [dbo].[sp_CleanupProcessedMessages]
    @DaysToKeep INT = 30
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@DaysToKeep, GETUTCDATE());
    
    BEGIN TRANSACTION;
    
    BEGIN TRY
        -- Delete old processed messages
        DELETE FROM ProcessedMessages
        WHERE ProcessedAt < @CutoffDate
            AND BatchId IN (SELECT BatchId FROM Batches WHERE Status = 'Completed');
        
        DECLARE @DeletedCount INT = @@ROWCOUNT;
        
        COMMIT TRANSACTION;
        
        SELECT @DeletedCount AS DeletedRecords;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
/****** Object:  StoredProcedure [dbo].[sp_GetBatchStatistics]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE PROCEDURE [dbo].[sp_GetBatchStatistics]
    @BatchId VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        b.BatchId,
        b.TotalChunks,
        b.ReceivedChunks,
        b.ProcessedChunks,
        b.Status,
        b.CreatedAt,
        b.CompletedAt,
        COUNT(DISTINCT r.RecordId) AS TotalRecords,
        DATEDIFF(SECOND, b.CreatedAt, ISNULL(b.CompletedAt, GETUTCDATE())) AS DurationSeconds
    FROM Batches b
    LEFT JOIN BulkUploadRecords r ON b.BatchId = r.BatchId
    WHERE b.BatchId = @BatchId
    GROUP BY 
        b.BatchId, b.TotalChunks, b.ReceivedChunks, b.ProcessedChunks, 
        b.Status, b.CreatedAt, b.CompletedAt;
END;
GO
/****** Object:  StoredProcedure [dbo].[sp_GetFailedChunks]    Script Date: 11-11-2025 19:45:00 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- Get failed chunks for retry
CREATE PROCEDURE [dbo].[sp_GetFailedChunks]
    @BatchId VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        bc.BatchId,
        bc.ChunkIndex,
        bc.ReceivedAt,
        bc.ProcessedAt,
        fm.ErrorMessage,
        fm.FailedAt,
        fm.Payload
    FROM BatchChunks bc
    LEFT JOIN FailedMessages fm ON bc.BatchId = fm.BatchId AND bc.ChunkIndex = fm.ChunkIndex
    WHERE bc.Status = 'Failed'
        AND (@BatchId IS NULL OR bc.BatchId = @BatchId)
    ORDER BY bc.BatchId, bc.ChunkIndex;
END;
GO
USE [master]
GO
ALTER DATABASE [BulkUploadDB] SET  READ_WRITE 
GO
