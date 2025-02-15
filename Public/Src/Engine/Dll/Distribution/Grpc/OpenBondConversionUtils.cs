// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Cache.Fingerprints;
using Google.Protobuf;

namespace BuildXL.Engine.Distribution.Grpc
{
    internal static class OpenBondConversionUtils
    {
        #region AttachCompletionInfo

        public static AttachCompletionInfo ToGrpc(this OpenBond.AttachCompletionInfo message)
        {
            return new AttachCompletionInfo()
            {
                WorkerId = message.WorkerId,

                AvailableRamMb = message.AvailableRamMb ?? 0,
                AvailableCommitMb = message.AvailableCommitMb ?? 0,
                MaxProcesses = message.MaxProcesses,
                MaxMaterialize = message.MaxMaterialize,
                MaxCacheLookup = message.MaxCacheLookup,
                WorkerCacheValidationContentHash = message.WorkerCacheValidationContentHash.Data.ToByteString(),
            };
        }

        public static OpenBond.AttachCompletionInfo ToOpenBond(this AttachCompletionInfo message)
        {
            return new OpenBond.AttachCompletionInfo()
            {
                WorkerId = message.WorkerId,
                
                AvailableRamMb = message.AvailableRamMb,
                AvailableCommitMb = message.AvailableCommitMb,
                MaxProcesses = message.MaxProcesses,
                MaxMaterialize = message.MaxMaterialize,
                MaxCacheLookup = message.MaxCacheLookup,
                WorkerCacheValidationContentHash = message.WorkerCacheValidationContentHash.ToBondContentHash(),
            };
        }
        #endregion

        #region WorkerNotificationArgs

        public static WorkerNotificationArgs ToGrpc(this OpenBond.WorkerNotificationArgs message)
        {
            var workerNotificationArgs = new WorkerNotificationArgs()
            {
                WorkerId = message.WorkerId,
                ExecutionLogBlobSequenceNumber = message.ExecutionLogBlobSequenceNumber,
                ExecutionLogData = message.ExecutionLogData.ToByteString(),
            };

            foreach (var i in message.CompletedPips)
            {
                workerNotificationArgs.CompletedPips.Add(new PipCompletionData()
                {
                    ExecuteStepTicks = i.ExecuteStepTicks,
                    PipIdValue = i.PipIdValue,
                    QueueTicks = i.QueueTicks,
                    ResultBlob = i.ResultBlob.ToByteString(),
                    Step = i.Step,
                    ThreadId = i.ThreadId,
                    StartTimeTicks = i.StartTimeTicks
                });
            }

            foreach (var i in message.ForwardedEvents)
            {
                var eventMessage = new EventMessage()
                {
                    EventId = i.EventId,
                    EventKeywords = i.EventKeywords,
                    EventName = i.EventName,
                    Id = i.Id,
                    Level = i.Level,
                    Text = i.Text,
                };

                if (i.PipProcessErrorEvent != null)
                {
                    eventMessage.PipProcessErrorEvent = new global::BuildXL.Distribution.Grpc.PipProcessErrorEvent()
                    {
                        PipSemiStableHash = i.PipProcessErrorEvent.PipSemiStableHash,
                        PipDescription = i.PipProcessErrorEvent.PipDescription,
                        PipSpecPath = i.PipProcessErrorEvent.PipSpecPath,
                        PipWorkingDirectory = i.PipProcessErrorEvent.PipWorkingDirectory,
                        PipExe = i.PipProcessErrorEvent.PipExe,
                        OutputToLog = i.PipProcessErrorEvent.OutputToLog,
                        MessageAboutPathsToLog = i.PipProcessErrorEvent.MessageAboutPathsToLog,
                        PathsToLog = i.PipProcessErrorEvent.PathsToLog,
                        ExitCode = i.PipProcessErrorEvent.ExitCode,
                        OptionalMessage = i.PipProcessErrorEvent.OptionalMessage,
                        ShortPipDescription = i.PipProcessErrorEvent.ShortPipDescription
                    };
                }

                workerNotificationArgs.ForwardedEvents.Add(eventMessage);
            }

            return workerNotificationArgs;
        }

        public static OpenBond.WorkerNotificationArgs ToOpenBond(this WorkerNotificationArgs message)
        {
            var completedPips = new List<OpenBond.PipCompletionData>();
            foreach (var i in message.CompletedPips)
            {
                completedPips.Add(new OpenBond.PipCompletionData()
                {
                    ExecuteStepTicks = i.ExecuteStepTicks,
                    PipIdValue = i.PipIdValue,
                    QueueTicks = i.QueueTicks,
                    ResultBlob = i.ResultBlob.ToArraySegmentByte(),
                    Step = i.Step,
                    ThreadId = i.ThreadId,
                    StartTimeTicks = i.StartTimeTicks
                });
            }

            var eventMessages = new List<OpenBond.EventMessage>();
            foreach (var i in message.ForwardedEvents)
            {
                eventMessages.Add(new OpenBond.EventMessage()
                {
                    EventId = i.EventId,
                    EventKeywords = i.EventKeywords,
                    EventName = i.EventName,
                    Id = i.Id,
                    Level = i.Level,
                    Text = i.Text,
                    PipProcessErrorEvent = i.ErrorEventCase == EventMessage.ErrorEventOneofCase.PipProcessErrorEvent ? new OpenBond.PipProcessErrorEvent()
                    {
                        PipSemiStableHash = i.PipProcessErrorEvent.PipSemiStableHash,
                        PipDescription = i.PipProcessErrorEvent.PipDescription,
                        PipSpecPath = i.PipProcessErrorEvent.PipSpecPath,
                        PipWorkingDirectory = i.PipProcessErrorEvent.PipWorkingDirectory,
                        PipExe = i.PipProcessErrorEvent.PipExe,
                        OutputToLog = i.PipProcessErrorEvent.OutputToLog,
                        MessageAboutPathsToLog = i.PipProcessErrorEvent.MessageAboutPathsToLog,
                        PathsToLog = i.PipProcessErrorEvent.PathsToLog,
                        ExitCode = i.PipProcessErrorEvent.ExitCode,
                        OptionalMessage = i.PipProcessErrorEvent.OptionalMessage,
                        ShortPipDescription = i.PipProcessErrorEvent.ShortPipDescription
                    } : null,
                });
            }

            return new OpenBond.WorkerNotificationArgs()
            {
                WorkerId = message.WorkerId,

                CompletedPips = completedPips,
                ExecutionLogBlobSequenceNumber = message.ExecutionLogBlobSequenceNumber,
                ExecutionLogData = message.ExecutionLogData.ToArraySegmentByte(),
                ForwardedEvents = eventMessages
            };
        }
        #endregion

        #region BuildStartData
        public static BuildStartData ToGrpc(this OpenBond.BuildStartData message)
        {
            var buildStartData = new BuildStartData()
            {
                WorkerId = message.WorkerId,
                CachedGraphDescriptor = new BuildXL.Distribution.Grpc.PipGraphCacheDescriptor()
                {
                    Id = message.CachedGraphDescriptor.Id,
                    TraceInfo = message.CachedGraphDescriptor.TraceInfo,
                    ConfigState = message.CachedGraphDescriptor.ConfigState?.Data.ToByteString() ?? ByteString.Empty,
                    DirectedGraph = message.CachedGraphDescriptor.DirectedGraph?.Data.ToByteString() ?? ByteString.Empty,
                    EngineState = message.CachedGraphDescriptor.EngineState?.Data.ToByteString() ?? ByteString.Empty,
                    HistoricTableSizes = message.CachedGraphDescriptor.HistoricTableSizes?.Data.ToByteString() ?? ByteString.Empty,
                    MountPathExpander = message.CachedGraphDescriptor.MountPathExpander?.Data.ToByteString() ?? ByteString.Empty,
                    PathTable = message.CachedGraphDescriptor.PathTable?.Data.ToByteString() ?? ByteString.Empty,
                    PipGraph = message.CachedGraphDescriptor.PipGraph?.Data.ToByteString() ?? ByteString.Empty,
                    PipGraphId = message.CachedGraphDescriptor.PipGraphId?.Data.ToByteString() ?? ByteString.Empty,
                    PipTable = message.CachedGraphDescriptor.PipTable?.Data.ToByteString() ?? ByteString.Empty,
                    PreviousInputs = message.CachedGraphDescriptor.PreviousInputs?.Data.ToByteString() ?? ByteString.Empty,
                    QualifierTable = message.CachedGraphDescriptor.QualifierTable?.Data.ToByteString() ?? ByteString.Empty,
                    StringTable = message.CachedGraphDescriptor.StringTable?.Data.ToByteString() ?? ByteString.Empty,
                    SymbolTable = message.CachedGraphDescriptor.SymbolTable?.Data.ToByteString() ?? ByteString.Empty,
                },
                FingerprintSalt = message.FingerprintSalt,
                OrchestratorLocation = new ServiceLocation()
                {
                    IpAddress = message.OrchestratorLocation.IpAddress,
                    Port = message.OrchestratorLocation.Port
                },
                SessionId = message.SessionId,
                SymlinkFileContentHash = message.SymlinkFileContentHash.Data.ToByteString()
            };

            foreach (var kvp in message.EnvironmentVariables)
            {
                buildStartData.EnvironmentVariables.Add(kvp.Key, kvp.Value);
            }

            return buildStartData;
        }

        public static OpenBond.BuildStartData ToOpenBond(this BuildStartData message)
        {
            return new OpenBond.BuildStartData()
            {
                WorkerId = message.WorkerId,
                CachedGraphDescriptor = new Cache.Fingerprints.PipGraphCacheDescriptor()
                {
                    ConfigState = message.CachedGraphDescriptor.ConfigState.ToBondContentHash(),
                    DirectedGraph = message.CachedGraphDescriptor.DirectedGraph.ToBondContentHash(),
                    EngineState = message.CachedGraphDescriptor.EngineState.ToBondContentHash(),
                    HistoricTableSizes = message.CachedGraphDescriptor.HistoricTableSizes.ToBondContentHash(),
                    Id = message.CachedGraphDescriptor.Id,
                    MountPathExpander = message.CachedGraphDescriptor.MountPathExpander.ToBondContentHash(),
                    PathTable = message.CachedGraphDescriptor.PathTable.ToBondContentHash(),
                    PipGraph = message.CachedGraphDescriptor.PipGraph.ToBondContentHash(),
                    PipGraphId = message.CachedGraphDescriptor.PipGraphId.ToBondContentHash(),
                    PipTable = message.CachedGraphDescriptor.PipTable.ToBondContentHash(),
                    PreviousInputs = message.CachedGraphDescriptor.PreviousInputs.ToBondContentHash(),
                    QualifierTable = message.CachedGraphDescriptor.QualifierTable.ToBondContentHash(),
                    StringTable = message.CachedGraphDescriptor.StringTable.ToBondContentHash(),
                    SymbolTable = message.CachedGraphDescriptor.SymbolTable.ToBondContentHash(),
                    TraceInfo = message.CachedGraphDescriptor.TraceInfo
                },
                EnvironmentVariables = message.EnvironmentVariables.ToDictionary(a => a.Key, a => a.Value),
                FingerprintSalt = message.FingerprintSalt,
                OrchestratorLocation = new OpenBond.ServiceLocation()
                {
                    IpAddress = message.OrchestratorLocation.IpAddress,
                    Port = message.OrchestratorLocation.Port
                },
                SessionId = message.SessionId,
                SymlinkFileContentHash = message.SymlinkFileContentHash.ToBondContentHash(),
            };
        }
        #endregion

        #region PipBuildRequest
        public static PipBuildRequest ToGrpc(this OpenBond.PipBuildRequest message)
        {
            var pipBuildRequest = new PipBuildRequest();

            foreach (var i in message.Hashes)
            {
                var fileArtifactKeyedHash = new FileArtifactKeyedHash()
                {
                    ContentHash = i.ContentHash.Data.ToByteString(),
                    FileName = i.FileName ?? string.Empty,
                    Length = i.Length,
                    PathString = i.PathString ?? string.Empty,
                    PathValue = i.PathValue,
                    ReparsePointType = (FileArtifactKeyedHash.Types.GrpcReparsePointType)i.ReparsePointType,
                    RewriteCount = i.RewriteCount,
                    IsSourceAffected = i.IsSourceAffected,
                    IsAllowedFileRewrite = i.IsAllowedFileRewrite,
                };

                if (i.ReparsePointTarget != null)
                {
                    fileArtifactKeyedHash.ReparsePointTarget = i.ReparsePointTarget;
                }

                if (i.AssociatedDirectories != null)
                {
                    foreach (var j in i.AssociatedDirectories)
                    {
                        fileArtifactKeyedHash.AssociatedDirectories.Add(new GrpcDirectoryArtifact()
                        {
                            DirectoryPathValue = j.DirectoryPathValue,
                            DirectorySealId = j.DirectorySealId,
                            IsDirectorySharedOpaque = j.IsDirectorySharedOpaque,
                        });
                    }
                }

                pipBuildRequest.Hashes.Add(fileArtifactKeyedHash);
            }

            foreach (var i in message.Pips)
            {
                var singlePipBuildRequest = new SinglePipBuildRequest()
                {
                    ActivityId = i.ActivityId,
                    ExpectedPeakWorkingSetMb = i.ExpectedPeakWorkingSetMb,
                    ExpectedAverageWorkingSetMb = i.ExpectedAverageWorkingSetMb,
                    ExpectedPeakCommitSizeMb = i.ExpectedPeakCommitSizeMb,
                    ExpectedAverageCommitSizeMb = i.ExpectedAverageCommitSizeMb,
                    Fingerprint = i.Fingerprint.Data.ToByteString(),
                    PipIdValue = i.PipIdValue,
                    Priority = i.Priority,
                    SequenceNumber = i.SequenceNumber,
                    Step = i.Step
                };

                pipBuildRequest.Pips.Add(singlePipBuildRequest);
            }

            return pipBuildRequest;
        }

        public static OpenBond.PipBuildRequest ToOpenBond(this PipBuildRequest message)
        {
            var hashes = new List<OpenBond.FileArtifactKeyedHash>();
            foreach (var i in message.Hashes)
            {
                var bondDirectories = new List<OpenBond.BondDirectoryArtifact>();
                foreach (var j in i.AssociatedDirectories)
                {
                    bondDirectories.Add(new OpenBond.BondDirectoryArtifact()
                    {
                        DirectoryPathValue = j.DirectoryPathValue,
                        DirectorySealId = j.DirectorySealId,
                        IsDirectorySharedOpaque = j.IsDirectorySharedOpaque
                    });
                }

                hashes.Add(new OpenBond.FileArtifactKeyedHash()
                {
                    AssociatedDirectories = bondDirectories,
                    ContentHash = i.ContentHash.ToBondContentHash(),
                    FileName = i.FileName ?? string.Empty,
                    Length = i.Length,
                    PathString = i.PathString,
                    PathValue = i.PathValue,
                    ReparsePointTarget = i.ReparsePointTarget,
                    ReparsePointType = (BondReparsePointType)((int)i.ReparsePointType),
                    RewriteCount = i.RewriteCount,
                    IsSourceAffected = i.IsSourceAffected,
                    IsAllowedFileRewrite = i.IsAllowedFileRewrite,
                });
            }

            var pips = new List<OpenBond.SinglePipBuildRequest>();
            foreach (var i in message.Pips)
            {
                pips.Add(new OpenBond.SinglePipBuildRequest()
                {
                    ActivityId = i.ActivityId,
                    ExpectedPeakWorkingSetMb = i.ExpectedPeakWorkingSetMb,
                    ExpectedAverageWorkingSetMb = i.ExpectedAverageWorkingSetMb,
                    ExpectedPeakCommitSizeMb = i.ExpectedPeakCommitSizeMb,
                    ExpectedAverageCommitSizeMb = i.ExpectedAverageCommitSizeMb,
                    Fingerprint = new BondFingerprint() { Data = i.Fingerprint.ToArraySegmentByte() },
                    PipIdValue = i.PipIdValue,
                    Priority = i.Priority,
                    SequenceNumber = i.SequenceNumber,
                    Step = i.Step,
                });
            }

            return new OpenBond.PipBuildRequest()
            {
                Hashes = hashes,
                Pips = pips
            };
        }
        #endregion
    }
}