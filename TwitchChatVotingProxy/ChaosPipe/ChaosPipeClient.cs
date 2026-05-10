using System.IO.Pipes;
using System.Collections.Concurrent;
using System.Timers;
using Newtonsoft.Json;
using Serilog;
using Timer = System.Timers.Timer;

namespace TwitchChatVotingProxy.ChaosPipe
{
    enum ChannelPointCommandResolution
    {
        Success,
        KnownFailure,
        Unknown
    }

    sealed class ChannelPointCommandResult
    {
        public ChannelPointCommandResolution Resolution { get; }
        public string? Message { get; }

        public ChannelPointCommandResult(ChannelPointCommandResolution resolution, string? message = null)
        {
            Resolution = resolution;
            Message = string.IsNullOrWhiteSpace(message) ? null : message;
        }
    }

    class ChaosPipeClient : IChaosPipeClient
    {
        /// <summary>
        /// Speed at which the chaos mod pipe gets processed
        /// </summary>
        private static readonly int PIPE_TICKRATE = 100;
        private static readonly int CHANNEL_POINTS_COMMAND_TIMEOUT_MS = 5000;

        public bool GotHelloBack { get; private set; } = false;

        public event EventHandler<OnGetCurrentVotesArgs>? OnGetCurrentVotes = null;
        public event EventHandler<OnGetVoteResultArgs>? OnGetVoteResult = null;
        public event EventHandler<OnNewVoteArgs>? OnNewVote = null;
        public event EventHandler<OnSetVotingModeArgs>? OnSetVotingMode = null;
        public event EventHandler<OnSetChannelPointsPausedArgs>? OnSetChannelPointsPaused = null;
        public event EventHandler? OnNoVotingRound = null;

        private readonly ILogger m_Logger = Log.Logger.ForContext<ChaosPipeClient>();
        private readonly NamedPipeClientStream m_Pipe = new(
            ".",
            "ChaosModVVotingPipe",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        private readonly StreamReader? m_PipeReader = null;
        private Task? m_PipeTick;
        private readonly StreamWriter? m_PipeWriter = null;
        private Task<string?>? m_ReadPipeTask = null;
        private readonly object m_PipeWriteLock = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ChannelPointCommandResult>>
            m_PendingChannelPointCommands = new();
        private long m_ChannelPointCommandIdCounter = 0;

        private readonly CancellationTokenSource m_CancellationTokenSource = new();
        private class PipeMessage
        {
            public string? Identifier { get; set; } = null;
            public List<string>? Options { get; set; } = null;
        }

        public class CurrentVotesResult
        {
            public string Identifier { get; } = "currentvotes";
            public List<int> Votes { get; }

            public CurrentVotesResult(List<int> votes)
            {
                Votes = votes;
            }
        }

        public class VoteResultObject
        {
            public string Identifier { get; } = "voteresult";
            public int? SelectedOption { get; }

            public VoteResultObject(int? selectedOption)
            {
                SelectedOption = selectedOption;
            }
        }

        public class ErrorObject
        {
            public string Identifier { get; } = "error";
            public string Message { get; }

            public ErrorObject(string message)
            {
                Message = $"{message} Reverting to normal mode.";
            }
        }

        public class PipeCommandObject
        {
            public string Identifier { get; }
            public List<string> Options { get; }

            public PipeCommandObject(string identifier, IEnumerable<string> options)
            {
                Identifier = identifier;
                Options = options.ToList();
            }
        }

        public ChaosPipeClient()
        {
            // Connect to the chaos mod pipe
            try
            {
                m_Pipe.Connect(1000);
                m_PipeReader = new StreamReader(m_Pipe);
                m_PipeWriter = new StreamWriter(m_Pipe)
                {
                    AutoFlush = true
                };

                m_Logger.Information("Successfully connected to chaos mod pipe");

                var ct = m_CancellationTokenSource.Token;

                m_PipeTick = Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        PipeTick(null, null);
                        await Task.Delay(PIPE_TICKRATE);
                    }
                }, ct);
            }
            catch (Exception exception)
            {
                m_Logger.Fatal(exception, "Failed to connect to chaos mod pipe, aborting");
                return;
            }
        }

        /// <summary>
        /// Check if the chaos mod pipe is still connected
        /// </summary>
        /// <returns>If the chaos mod pipe is still connected</returns>
        public bool IsConnected()
        {
            return m_Pipe.IsConnected;
        }

        /// <summary>
        /// Disconnects the stream reader/writer and the pipe itself
        /// </summary>
        private void DisconnectFromPipe()
        {
            m_CancellationTokenSource.Cancel();
            ResolvePendingChannelPointCommands(new ChannelPointCommandResult(
                ChannelPointCommandResolution.Unknown,
                "Chaos Mod disconnected before the channel point action could be confirmed."));

            try
            {
                m_PipeReader?.Close();
                m_PipeWriter?.Close();
            }
            catch (ObjectDisposedException)
            {

            }

            m_Pipe.Close();
        }

        private void GetCurrentVotes()
        {
            var args = new OnGetCurrentVotesArgs();
            OnGetCurrentVotes?.Invoke(this, args);
            if (args.CurrentVotes == null)
                m_Logger.Error("Listeners failed to supply on get current vote args");
            else
            {
                CurrentVotesResult res = new(args.CurrentVotes);
                SendMessageToPipe(JsonConvert.SerializeObject(res));
            }
        }
        /// <summary>
        /// Gets called when the chaos mod requests vote results
        /// </summary>
        private void GetVoteResult()
        {
            m_Logger.Debug("Asking listeners for vote result");
            var e = new OnGetVoteResultArgs();
            // Dispatch information to listeners
            OnGetVoteResult?.Invoke(this, e);
            // Send the chosen option to the pipe
            if (e.ChosenOption == null)
            {
                m_Logger.Warning("Get vote result did not update chosen option, using 0 (first option)");
                e.ChosenOption = 0;
            }
            VoteResultObject result = new(e.ChosenOption);
            SendMessageToPipe(JsonConvert.SerializeObject(result));
            m_Logger.Debug($"Vote result sent to pipe: {e.ChosenOption}");
        }
        /// <summary>
        /// Gets called every pipe tick
        /// </summary>
        private void PipeTick(object? sender, ElapsedEventArgs? e)
        {
            try
            {
                SendHeartBeat();
                ReadPipe();
            }
            catch (Exception exception)
            {
                m_Logger.Information("Pipe disconnected: " + exception.Message);
                DisconnectFromPipe();
            }
        }
        /// <summary>
        /// Reads the contents of the chaos mod pipe and evaluates its message
        /// </summary>
        private void ReadPipe()
        {
            // If no reading task is active, create one
            if (m_ReadPipeTask == null)
                m_ReadPipeTask = m_PipeReader?.ReadLineAsync();
            // If the reading task is created and complete, get its results
            else if (m_ReadPipeTask.IsCompleted)
            {
                // Get the message from the pipe read
                var message = m_ReadPipeTask.Result;
                if (message is not null)
                {
                    // Null the reading task so the next read is dispatched
                    m_ReadPipeTask = null;

                    // Evaluate message
                    var pipe = JsonConvert.DeserializeObject<PipeMessage>(message);
                    switch (pipe?.Identifier)
                    {
                    case "hello_back":
                        GotHelloBack = true;
                        break;
                    case "vote":
                        StartNewVote(pipe.Options);
                        break;
                    case "votingmode":
                        ChangeVotingMode(pipe.Options);
                        break;
                    case "getvoteresult":
                        GetVoteResult();
                        break;
                    case "novoteround":
                        StartNoVotingRound();
                        break;
                    case "getcurrentvotes":
                        GetCurrentVotes();
                        break;
                    case "channel_points_result":
                        HandleChannelPointsResult(pipe.Options);
                        break;
                    case "set_channel_points_paused":
                        HandleSetChannelPointsPaused(pipe.Options);
                        break;
                    default:
                        m_Logger.Warning($"Unknown request: {message}");
                        break;
                    }
                }
            }
        }
        /// <summary>
        /// Sends a message to the chaos mod pipe
        /// </summary>
        /// <param name="message">Message to be sent</param>
        public void SendMessageToPipe(string message)
        {
            if (m_Pipe.IsConnected)
            {
                lock (m_PipeWriteLock)
                {
                    m_PipeWriter?.Write($"{message}\0");
                    m_Pipe.WaitForPipeDrain();
                }
            }
        }

        public void SendPipeCommand(string identifier, params string[] options)
        {
            SendMessageToPipe(JsonConvert.SerializeObject(new PipeCommandObject(identifier, options)));
        }

        public async Task<ChannelPointCommandResult> SendChannelPointCommandAsync(string identifier, params string[] options)
        {
            if (!m_Pipe.IsConnected)
            {
                return new ChannelPointCommandResult(
                    ChannelPointCommandResolution.KnownFailure,
                    "Chaos Mod is not currently connected.");
            }

            var requestId = Interlocked.Increment(ref m_ChannelPointCommandIdCounter).ToString();
            var resultTaskSource = new TaskCompletionSource<ChannelPointCommandResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!m_PendingChannelPointCommands.TryAdd(requestId, resultTaskSource))
            {
                return new ChannelPointCommandResult(
                    ChannelPointCommandResolution.KnownFailure,
                    "Failed to queue the channel point action request.");
            }

            try
            {
                SendPipeCommand(identifier, new[] { requestId }.Concat(options).ToArray());
            }
            catch (Exception exception)
            {
                m_PendingChannelPointCommands.TryRemove(requestId, out _);

                var resolution = m_Pipe.IsConnected
                    ? ChannelPointCommandResolution.Unknown
                    : ChannelPointCommandResolution.KnownFailure;
                var message = m_Pipe.IsConnected
                    ? "Failed to send the channel point action to Chaos Mod."
                    : "Chaos Mod disconnected before the channel point action could be sent.";

                m_Logger.Warning(exception, "Failed to send channel point command {Identifier}", identifier);
                return new ChannelPointCommandResult(resolution, message);
            }

            var completedTask = await Task.WhenAny(resultTaskSource.Task, Task.Delay(CHANNEL_POINTS_COMMAND_TIMEOUT_MS));
            if (completedTask != resultTaskSource.Task)
            {
                if (m_PendingChannelPointCommands.TryRemove(requestId, out _))
                {
                    return new ChannelPointCommandResult(
                        ChannelPointCommandResolution.Unknown,
                        "Chaos Mod did not confirm the channel point action in time.");
                }
            }

            return await resultTaskSource.Task;
        }
        /// <summary>
        /// Is called when the chaos mod starts a new vote
        /// </summary>
        /// <param name="message">Message from the pipe to be parsed into votes</param>
        private void StartNewVote(List<string>? options)
        {
            if (options is null)
                return;

            // Dispatch information to listeners
            OnNewVote?.Invoke(this, new OnNewVoteArgs(options.ToArray()));
        }
        private void ChangeVotingMode(List<string>? options)
        {
            if (options == null || options.Count == 0)
                return;

            string modeName = options[0];

            EVotingMode? mode = VotingMode.ReverseLookup(modeName);
            if (mode == null)
            {
                m_Logger.Error("Unknown voting mode: " + modeName);
                return;
            }

            m_Logger.Information("Setting voting mode to " + modeName);
            OnSetVotingMode?.Invoke(this, new((EVotingMode)mode));
        }
        /// <summary>
        /// Start a no-voting round. The chaos mod will decide over the options
        /// </summary>
        private void StartNoVotingRound()
        {
            OnNoVotingRound?.Invoke(this, new());
        }

        private void HandleChannelPointsResult(List<string>? options)
        {
            if (options == null || options.Count < 2)
            {
                m_Logger.Warning("Received malformed channel point command result from the pipe");
                return;
            }

            var requestId = options[0];
            if (!m_PendingChannelPointCommands.TryRemove(requestId, out var resultTaskSource))
                return;

            var resolution = options[1] switch
            {
                "success" => ChannelPointCommandResolution.Success,
                "failed" => ChannelPointCommandResolution.KnownFailure,
                _ => ChannelPointCommandResolution.Unknown
            };
            var message = options.Count > 2 ? options[2] : null;
            resultTaskSource.TrySetResult(new ChannelPointCommandResult(resolution, message));
        }

        private void HandleSetChannelPointsPaused(List<string>? options)
        {
            if (options == null || options.Count == 0)
            {
                m_Logger.Warning("Received malformed channel points pause state from the pipe");
                return;
            }

            var paused = options[0] == "1"
                || string.Equals(options[0], "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(options[0], "paused", StringComparison.OrdinalIgnoreCase);
            OnSetChannelPointsPaused?.Invoke(this, new OnSetChannelPointsPausedArgs(paused));
        }

        private void ResolvePendingChannelPointCommands(ChannelPointCommandResult result)
        {
            foreach (var pendingRequest in m_PendingChannelPointCommands.ToArray())
            {
                if (m_PendingChannelPointCommands.TryRemove(pendingRequest.Key, out var resultTaskSource))
                    resultTaskSource.TrySetResult(result);
            }
        }
        /// <summary>
        /// Sends a heartbeat to the chaos mod
        /// </summary>
        private void SendHeartBeat()
        {
            SendMessageToPipe("ping");
        }
        /// <summary>
        /// Sends an error message to the mod and stops voting mode
        /// </summary>
        /// <param name="message">Message to be sent</param>
        public void SendErrorMessage(string message)
        {
            var error = new ErrorObject(message);
            SendMessageToPipe(JsonConvert.SerializeObject(error));

            DisconnectFromPipe();
        }
    }
}
