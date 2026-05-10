using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;
using Serilog;
using Shared;
using TwitchChatVotingProxy.ChaosPipe;

namespace TwitchChatVotingProxy.VotingReceiver
{
    class TwitchChannelPointsReceiver
    {
        private const string EVENTSUB_WEBSOCKET_URL = "wss://eventsub.wss.twitch.tv/ws";
        private const string TWITCH_HELIX_BASE_URL = "https://api.twitch.tv/helix";
        private const int MAX_HANDLED_MESSAGE_IDS = 256;
        private const int RECONNECT_DELAY_MS = 5000;
        private const int STARTUP_TIMEOUT_MS = 15000;
        private const int MAX_REWARD_TITLE_LENGTH = 45;
        private const int MAX_REWARD_PROMPT_LENGTH = 200;
        private const string DEFAULT_EFFECT_REWARD_PROMPT = "Immediately invoke this effect in the Chaos Mod.";

        private sealed class ManagedRewardDefinition
        {
            public required TwitchChannelPointRewardMapping Mapping { get; init; }
            public required string Title { get; init; }
            public required string Prompt { get; init; }
            public required int Cost { get; init; }
        }

        private sealed class ManagedRewardInfo
        {
            public required string Id { get; init; }
            public required string Title { get; init; }
            public string Prompt { get; init; } = string.Empty;
            public int Cost { get; init; }
        }

        private sealed class ManagedRewardRedemptionInfo
        {
            public required string Id { get; init; }
        }

        private readonly OptionsFile m_Config;
        private readonly string? m_ChannelName = null;
        private readonly string? m_ClientId = null;
        private readonly string? m_OAuth = null;
        private readonly string? m_ManagedEffectRewardPrompt = null;

        private readonly ChaosPipeClient m_ChaosPipe;
        private readonly HttpClient m_HttpClient = new();
        private readonly ILogger m_Logger = Log.Logger.ForContext<TwitchChannelPointsReceiver>();

        private readonly List<TwitchChannelPointRewardMapping> m_Mappings = new();
        private readonly Dictionary<string, TwitchChannelPointRewardMapping> m_MappingsByRewardId = new();
        private readonly List<TwitchChannelPointRewardMapping> m_MappingsByTitle = new();
        private readonly HashSet<string> m_ManagedRewardIds = new();
        private readonly HashSet<string> m_HandledMessageIds = new();
        private readonly Queue<string> m_HandledMessageIdsQueue = new();
        private readonly SemaphoreSlim m_RewardSyncLock = new(1, 1);

        private CancellationTokenSource? m_RunCancellationTokenSource = null;
        private string? m_BroadcasterUserId = null;
        private string? m_NormalizedOAuth = null;
        private TaskCompletionSource<bool>? m_ReadyTaskSource = null;

        public TwitchChannelPointsReceiver(OptionsFile config, ChaosPipeClient chaosPipe)
        {
            m_Config = config;
            m_ChannelName = config.ReadValue<string>("TwitchChannelName");
            m_ClientId = config.ReadValue<string>("TwitchChannelPointsClientId");
            m_OAuth = config.ReadValue<string>("TwitchChannelPointsOAuth");
            m_ManagedEffectRewardPrompt = config.ReadValue("TwitchChannelPointsRewardDescription", DEFAULT_EFFECT_REWARD_PROMPT);
            m_ChaosPipe = chaosPipe;

            var mappings = config.ReadValue("TwitchChannelPointRewardMappings", new List<TwitchChannelPointRewardMapping>())
                ?? new List<TwitchChannelPointRewardMapping>();
            foreach (var mapping in mappings)
                m_Mappings.Add(mapping);

            var managedRewardIds = config.ReadValue("TwitchManagedChannelPointRewardIds", new List<string>())
                ?? new List<string>();
            foreach (var managedRewardId in managedRewardIds)
            {
                if (!string.IsNullOrWhiteSpace(managedRewardId))
                    m_ManagedRewardIds.Add(managedRewardId);
            }

            RebuildMappingIndexes();
        }

        public async Task<bool> Init()
        {
            if (string.IsNullOrWhiteSpace(m_ChannelName))
            {
                m_Logger.Fatal("Twitch channel is not set for channel points!");
                m_ChaosPipe.SendErrorMessage("Twitch channel is not set. Please set one in the config utility.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(m_ClientId))
            {
                m_Logger.Fatal("Twitch client ID is not set for channel points!");
                m_ChaosPipe.SendErrorMessage("Twitch client ID is not set for channel points. Please set one in the config utility.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(m_OAuth))
            {
                m_Logger.Fatal("Twitch OAuth token is not set for channel points!");
                m_ChaosPipe.SendErrorMessage("Twitch OAuth token is not set for channel points. Please set one in the config utility.");
                return false;
            }

            m_NormalizedOAuth = NormalizeOAuthToken(m_OAuth);
            m_BroadcasterUserId = await ResolveBroadcasterUserId();
            if (string.IsNullOrWhiteSpace(m_BroadcasterUserId))
            {
                m_Logger.Fatal("Failed to resolve broadcaster user ID for Twitch channel points");
                m_ChaosPipe.SendErrorMessage("Failed to resolve Twitch broadcaster information for channel points. Please verify the channel name, client ID, and OAuth token.");
                return false;
            }

            var rewardCount = await SyncManagedRewards(enabled: true, paused: true, CancellationToken.None);
            if (rewardCount == 0)
            {
                m_Logger.Warning("Twitch channel points are enabled but no valid managed rewards are configured");
                return true;
            }

            m_ReadyTaskSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            m_RunCancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => RunAsync(m_RunCancellationTokenSource.Token));

            var completedTask = await Task.WhenAny(m_ReadyTaskSource.Task, Task.Delay(STARTUP_TIMEOUT_MS));
            if (completedTask != m_ReadyTaskSource.Task)
            {
                m_Logger.Error("Timed out while initializing Twitch channel points receiver");
                return false;
            }

            return await m_ReadyTaskSource.Task;
        }

        public async Task SetManagedRewardsPaused(bool paused)
        {
            if (string.IsNullOrWhiteSpace(m_BroadcasterUserId) || string.IsNullOrWhiteSpace(m_NormalizedOAuth))
                return;

            await SyncManagedRewards(enabled: true, paused: paused, CancellationToken.None);
        }

        public async Task DisableManagedRewards()
        {
            if (string.IsNullOrWhiteSpace(m_BroadcasterUserId) || string.IsNullOrWhiteSpace(m_NormalizedOAuth))
                return;

            await SyncManagedRewards(enabled: false, paused: true, CancellationToken.None);
        }

        private async Task<int> SyncManagedRewards(bool enabled, bool paused, CancellationToken cancellationToken)
        {
            var desiredRewards = BuildDesiredManagedRewards();

            await m_RewardSyncLock.WaitAsync(cancellationToken);
            try
            {
                var existingRewards = await GetManagedCustomRewards(cancellationToken);
                var usedRewardIds = new HashSet<string>(StringComparer.Ordinal);
                var retainedManagedRewardIds = new HashSet<string>(StringComparer.Ordinal);

                foreach (var desiredReward in desiredRewards)
                {
                    var existingReward = FindExistingManagedReward(desiredReward, existingRewards, usedRewardIds);
                    if (existingReward != null)
                    {
                        await UpdateCustomReward(existingReward.Id, desiredReward.Title, desiredReward.Prompt, desiredReward.Cost,
                            enabled: enabled, paused: paused, cancellationToken);

                        desiredReward.Mapping.RewardId = existingReward.Id;
                        retainedManagedRewardIds.Add(existingReward.Id);
                        usedRewardIds.Add(existingReward.Id);
                    }
                    else if (enabled)
                    {
                        var createdReward = await CreateCustomReward(desiredReward, paused, cancellationToken);
                        desiredReward.Mapping.RewardId = createdReward.Id;
                        retainedManagedRewardIds.Add(createdReward.Id);
                        usedRewardIds.Add(createdReward.Id);
                    }

                    desiredReward.Mapping.RewardTitle = desiredReward.Title;
                }

                var trackedManagedRewardIds = new HashSet<string>(m_ManagedRewardIds, StringComparer.Ordinal);
                foreach (var mapping in m_Mappings)
                {
                    if (!string.IsNullOrWhiteSpace(mapping.RewardId))
                        trackedManagedRewardIds.Add(mapping.RewardId);
                }

                foreach (var obsoleteRewardId in trackedManagedRewardIds)
                {
                    if (retainedManagedRewardIds.Contains(obsoleteRewardId))
                        continue;

                    var existingReward = existingRewards.FirstOrDefault(reward =>
                        string.Equals(reward.Id, obsoleteRewardId, StringComparison.Ordinal));
                    if (existingReward == null)
                        continue;

                    await CancelUnfulfilledRedemptions(existingReward.Id, cancellationToken);
                    await DeleteCustomReward(existingReward.Id, cancellationToken);
                }

                m_ManagedRewardIds.Clear();
                foreach (var rewardId in retainedManagedRewardIds)
                    m_ManagedRewardIds.Add(rewardId);

                RebuildMappingIndexes();
                PersistManagedRewardConfig();

                m_Logger.Information("Managed Twitch rewards synchronized ({RewardCount} configured, enabled={Enabled}, paused={Paused})",
                    desiredRewards.Count, enabled, paused);
                return desiredRewards.Count;
            }
            finally
            {
                m_RewardSyncLock.Release();
            }
        }

        private List<ManagedRewardDefinition> BuildDesiredManagedRewards()
        {
            var desiredRewards = new List<ManagedRewardDefinition>();
            var titleOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in m_Mappings)
            {
                if (mapping.RewardCost <= 0)
                    continue;

                if (mapping.Action == TwitchChannelPointActionType.TriggerEffect && string.IsNullOrWhiteSpace(mapping.EffectId))
                {
                    m_Logger.Warning("Skipping managed Twitch reward with missing effect ID");
                    continue;
                }

                var baseTitle = BuildManagedRewardBaseTitle(mapping);
                if (string.IsNullOrWhiteSpace(baseTitle))
                    baseTitle = "Chaos Mod: Trigger Effect";

                titleOccurrences.TryGetValue(baseTitle, out var occurrence);
                occurrence++;
                titleOccurrences[baseTitle] = occurrence;

                var title = BuildUniqueRewardTitle(baseTitle, occurrence);
                desiredRewards.Add(new ManagedRewardDefinition()
                {
                    Mapping = mapping,
                    Title = title,
                    Prompt = BuildRewardPrompt(mapping),
                    Cost = mapping.RewardCost
                });
            }

            return desiredRewards;
        }

        private static string BuildManagedRewardBaseTitle(TwitchChannelPointRewardMapping mapping)
        {
            if (mapping.Action == TwitchChannelPointActionType.ClearEffects)
                return "Chaos Mod: Clear All Active Effects";

            if (!string.IsNullOrWhiteSpace(mapping.RewardTitle))
                return TrimDuplicateSuffix(mapping.RewardTitle.Trim());

            if (!string.IsNullOrWhiteSpace(mapping.EffectId))
                return $"Chaos Mod: {mapping.EffectId}";

            return "Chaos Mod: Trigger Effect";
        }

        private static string BuildUniqueRewardTitle(string baseTitle, int occurrence)
        {
            var suffix = occurrence > 1 ? $" #{occurrence}" : string.Empty;
            var maxBaseLength = MAX_REWARD_TITLE_LENGTH - suffix.Length;
            return TruncateWithEllipsis(baseTitle, maxBaseLength) + suffix;
        }

        private string BuildRewardPrompt(TwitchChannelPointRewardMapping mapping)
        {
            if (mapping.Action == TwitchChannelPointActionType.ClearEffects)
                return "Clears all currently active Chaos Mod effects.";

            var description = string.IsNullOrWhiteSpace(m_ManagedEffectRewardPrompt)
                ? DEFAULT_EFFECT_REWARD_PROMPT
                : m_ManagedEffectRewardPrompt.Trim();
            return TruncateWithEllipsis(description, MAX_REWARD_PROMPT_LENGTH);
        }

        private static string TrimDuplicateSuffix(string value)
        {
            var hashIndex = value.LastIndexOf(" #", StringComparison.Ordinal);
            if (hashIndex < 0 || hashIndex + 2 >= value.Length)
                return value;

            for (var index = hashIndex + 2; index < value.Length; index++)
            {
                if (!char.IsDigit(value[index]))
                    return value;
            }

            return value[..hashIndex];
        }

        private static string TruncateWithEllipsis(string value, int maxLength)
        {
            if (value.Length <= maxLength)
                return value;

            if (maxLength <= 3)
                return value[..maxLength];

            return value[..(maxLength - 3)] + "...";
        }

        private void PersistManagedRewardConfig()
        {
            m_Config.WriteValue("TwitchChannelPointRewardMappings", JArray.FromObject(m_Mappings));
            m_Config.WriteValue("TwitchManagedChannelPointRewardIds", JArray.FromObject(m_ManagedRewardIds.OrderBy(id => id).ToList()));
            m_Config.WriteFile();
        }

        private void RebuildMappingIndexes()
        {
            m_MappingsByRewardId.Clear();
            m_MappingsByTitle.Clear();

            foreach (var mapping in m_Mappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.RewardId))
                    m_MappingsByRewardId[mapping.RewardId] = mapping;
                if (!string.IsNullOrWhiteSpace(mapping.RewardTitle))
                    m_MappingsByTitle.Add(mapping);
            }
        }

        private async Task<List<ManagedRewardInfo>> GetManagedCustomRewards(CancellationToken cancellationToken)
        {
            var requestUri = $"{TWITCH_HELIX_BASE_URL}/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(m_BroadcasterUserId!)}&only_manageable_rewards=true";
            using var request = CreateHelixRequest(HttpMethod.Get, requestUri);

            var response = await m_HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Failed to list manageable Twitch rewards ({(int)response.StatusCode}): {responseBody}");
            }

            var json = JObject.Parse(responseBody);
            return json["data"]?
                .Select(ParseManagedReward)
                .Where(reward => reward != null)
                .Cast<ManagedRewardInfo>()
                .ToList()
                ?? new List<ManagedRewardInfo>();
        }

        private async Task<ManagedRewardInfo> CreateCustomReward(ManagedRewardDefinition reward, bool paused, CancellationToken cancellationToken)
        {
            var requestUri = $"{TWITCH_HELIX_BASE_URL}/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(m_BroadcasterUserId!)}";
            using var request = CreateHelixRequest(HttpMethod.Post, requestUri,
                BuildRewardRequestBody(reward.Title, reward.Prompt, reward.Cost, enabled: true, paused: paused));

            var response = await m_HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Failed to create managed Twitch reward ({(int)response.StatusCode}): {responseBody}");
            }

            var json = JObject.Parse(responseBody);
            var rewardToken = json["data"]?.FirstOrDefault()
                ?? throw new InvalidOperationException("Twitch reward creation did not return reward data.");
            return ParseManagedReward(rewardToken)
                ?? throw new InvalidOperationException("Twitch reward creation returned incomplete reward data.");
        }

        private async Task UpdateCustomReward(string rewardId, string title, string prompt, int cost, bool enabled, bool paused,
            CancellationToken cancellationToken)
        {
            var requestUri = $"{TWITCH_HELIX_BASE_URL}/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(m_BroadcasterUserId!)}&id={Uri.EscapeDataString(rewardId)}";
            using var request = CreateHelixRequest(HttpMethod.Patch, requestUri,
                BuildRewardRequestBody(title, prompt, cost, enabled, paused));

            var response = await m_HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Failed to update managed Twitch reward ({(int)response.StatusCode}): {responseBody}");
            }
        }

        private async Task DeleteCustomReward(string rewardId, CancellationToken cancellationToken)
        {
            var requestUri = $"{TWITCH_HELIX_BASE_URL}/channel_points/custom_rewards?broadcaster_id={Uri.EscapeDataString(m_BroadcasterUserId!)}&id={Uri.EscapeDataString(rewardId)}";
            using var request = CreateHelixRequest(HttpMethod.Delete, requestUri);

            var response = await m_HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return;

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Failed to delete managed Twitch reward ({(int)response.StatusCode}): {responseBody}");
            }
        }

        private async Task CancelUnfulfilledRedemptions(string rewardId, CancellationToken cancellationToken)
        {
            var redemptions = await GetUnfulfilledRedemptions(rewardId, cancellationToken);
            foreach (var redemption in redemptions)
                await UpdateRedemptionStatus(rewardId, redemption.Id, "CANCELED");
        }

        private async Task<List<ManagedRewardRedemptionInfo>> GetUnfulfilledRedemptions(string rewardId, CancellationToken cancellationToken)
        {
            var redemptions = new List<ManagedRewardRedemptionInfo>();
            string? afterCursor = null;

            do
            {
                var requestUri = new StringBuilder(
                    $"{TWITCH_HELIX_BASE_URL}/channel_points/custom_rewards/redemptions?broadcaster_id={Uri.EscapeDataString(m_BroadcasterUserId!)}&reward_id={Uri.EscapeDataString(rewardId)}&status=UNFULFILLED&sort=OLDEST&first=50");
                if (!string.IsNullOrWhiteSpace(afterCursor))
                    requestUri.Append($"&after={Uri.EscapeDataString(afterCursor)}");

                using var request = CreateHelixRequest(HttpMethod.Get, requestUri.ToString());

                var response = await m_HttpClient.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    break;

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        $"Failed to list unfulfilled redemptions for managed Twitch reward ({(int)response.StatusCode}): {responseBody}");
                }

                var json = JObject.Parse(responseBody);
                redemptions.AddRange(json["data"]?
                    .Select(redemptionToken => redemptionToken["id"]?.ToObject<string>())
                    .Where(redemptionId => !string.IsNullOrWhiteSpace(redemptionId))
                    .Select(redemptionId => new ManagedRewardRedemptionInfo()
                    {
                        Id = redemptionId!
                    })
                    .ToList()
                    ?? new List<ManagedRewardRedemptionInfo>());

                afterCursor = json["pagination"]?["cursor"]?.ToObject<string>();
            }
            while (!string.IsNullOrWhiteSpace(afterCursor));

            return redemptions;
        }

        private async Task UpdateRedemptionStatus(string rewardId, string redemptionId, string status)
        {
            var requestUri =
                $"{TWITCH_HELIX_BASE_URL}/channel_points/custom_rewards/redemptions?broadcaster_id={Uri.EscapeDataString(m_BroadcasterUserId!)}&reward_id={Uri.EscapeDataString(rewardId)}&id={Uri.EscapeDataString(redemptionId)}";
            using var request = CreateHelixRequest(HttpMethod.Patch, requestUri, new JObject()
            {
                ["status"] = status
            });

            var response = await m_HttpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Failed to update Twitch redemption status to {status} ({(int)response.StatusCode}): {responseBody}");
            }
        }

        private HttpRequestMessage CreateHelixRequest(HttpMethod method, string requestUri, JObject? body = null)
        {
            var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", m_NormalizedOAuth);
            request.Headers.Add("Client-Id", m_ClientId);
            if (body != null)
                request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

            return request;
        }

        private static JObject BuildRewardRequestBody(string title, string prompt, int cost, bool enabled, bool paused)
        {
            return new JObject()
            {
                ["title"] = title,
                ["prompt"] = prompt,
                ["cost"] = cost,
                ["is_enabled"] = enabled,
                ["is_paused"] = paused,
                ["should_redemptions_skip_request_queue"] = false
            };
        }

        private static ManagedRewardInfo? ParseManagedReward(JToken rewardToken)
        {
            var rewardId = rewardToken["id"]?.ToObject<string>();
            var title = rewardToken["title"]?.ToObject<string>();
            var cost = rewardToken["cost"]?.ToObject<int?>();
            if (string.IsNullOrWhiteSpace(rewardId) || string.IsNullOrWhiteSpace(title) || cost == null)
                return null;

            return new ManagedRewardInfo()
            {
                Id = rewardId,
                Title = title,
                Prompt = rewardToken["prompt"]?.ToObject<string>() ?? string.Empty,
                Cost = cost.Value
            };
        }

        private static ManagedRewardInfo? FindExistingManagedReward(ManagedRewardDefinition desiredReward,
            IEnumerable<ManagedRewardInfo> existingRewards, ISet<string> usedRewardIds)
        {
            if (!string.IsNullOrWhiteSpace(desiredReward.Mapping.RewardId))
            {
                var rewardById = existingRewards.FirstOrDefault(reward =>
                    !usedRewardIds.Contains(reward.Id)
                    && string.Equals(reward.Id, desiredReward.Mapping.RewardId, StringComparison.Ordinal));
                if (rewardById != null)
                    return rewardById;
            }

            return existingRewards.FirstOrDefault(reward =>
                !usedRewardIds.Contains(reward.Id)
                && string.Equals(reward.Title, desiredReward.Title, StringComparison.OrdinalIgnoreCase));
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var websocketUrl = EVENTSUB_WEBSOCKET_URL;
            var recreateSubscriptions = true;

            while (!cancellationToken.IsCancellationRequested && m_ChaosPipe.IsConnected())
            {
                try
                {
                    using var websocket = new ClientWebSocket();
                    await websocket.ConnectAsync(new Uri(websocketUrl), cancellationToken);

                    var sessionId = await WaitForWelcomeMessage(websocket, cancellationToken);
                    if (string.IsNullOrWhiteSpace(sessionId))
                        throw new InvalidOperationException("Twitch EventSub WebSocket welcome did not provide a session ID.");

                    if (recreateSubscriptions)
                        await CreateRedemptionSubscription(sessionId, cancellationToken);

                    m_ReadyTaskSource?.TrySetResult(true);

                    var reconnectUrl = await ReceiveLoop(websocket, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(reconnectUrl))
                    {
                        websocketUrl = reconnectUrl;
                        recreateSubscriptions = false;
                        continue;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    if (m_ReadyTaskSource != null && !m_ReadyTaskSource.Task.IsCompleted)
                        m_ReadyTaskSource.TrySetException(exception);
                    else
                        m_Logger.Warning(exception, "Twitch channel points connection failed, retrying");
                }

                websocketUrl = EVENTSUB_WEBSOCKET_URL;
                recreateSubscriptions = true;

                try
                {
                    await Task.Delay(RECONNECT_DELAY_MS, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task<string?> WaitForWelcomeMessage(ClientWebSocket websocket, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && websocket.State == WebSocketState.Open)
            {
                var json = await ReceiveJsonMessage(websocket, cancellationToken);
                var messageType = json["metadata"]?["message_type"]?.ToObject<string>();
                switch (messageType)
                {
                case "session_welcome":
                    m_Logger.Information("Successfully connected to Twitch EventSub WebSocket");
                    return json["payload"]?["session"]?["id"]?.ToObject<string>();
                case "revocation":
                    HandleRevocation(json);
                    return null;
                }
            }

            return null;
        }

        private async Task<string?> ReceiveLoop(ClientWebSocket websocket, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && websocket.State == WebSocketState.Open)
            {
                var json = await ReceiveJsonMessage(websocket, cancellationToken);
                var messageType = json["metadata"]?["message_type"]?.ToObject<string>();
                switch (messageType)
                {
                case "notification":
                    HandleNotification(json);
                    break;
                case "session_keepalive":
                    break;
                case "session_reconnect":
                    var reconnectUrl = json["payload"]?["session"]?["reconnect_url"]?.ToObject<string>();
                    if (!string.IsNullOrWhiteSpace(reconnectUrl))
                    {
                        m_Logger.Information("Twitch EventSub requested reconnect");
                        return reconnectUrl;
                    }
                    break;
                case "revocation":
                    HandleRevocation(json);
                    return null;
                }
            }

            return null;
        }

        private async Task<JObject> ReceiveJsonMessage(ClientWebSocket websocket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var stream = new MemoryStream();

            while (true)
            {
                var result = await websocket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("Twitch EventSub WebSocket closed the connection.");

                stream.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                    break;
            }

            return JObject.Parse(Encoding.UTF8.GetString(stream.ToArray()));
        }

        private async Task CreateRedemptionSubscription(string sessionId, CancellationToken cancellationToken)
        {
            using var request = CreateHelixRequest(HttpMethod.Post, $"{TWITCH_HELIX_BASE_URL}/eventsub/subscriptions", new JObject()
            {
                ["type"] = "channel.channel_points_custom_reward_redemption.add",
                ["version"] = "1",
                ["condition"] = new JObject()
                {
                    ["broadcaster_user_id"] = m_BroadcasterUserId
                },
                ["transport"] = new JObject()
                {
                    ["method"] = "websocket",
                    ["session_id"] = sessionId
                }
            });

            var response = await m_HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
            {
                throw new InvalidOperationException(
                    $"Failed to subscribe to Twitch channel point redemptions ({(int)response.StatusCode}): {responseBody}");
            }

            m_Logger.Information("Subscribed to Twitch channel point redemptions");
        }

        private async Task<string?> ResolveBroadcasterUserId()
        {
            using var request = CreateHelixRequest(HttpMethod.Get,
                $"{TWITCH_HELIX_BASE_URL}/users?login={Uri.EscapeDataString(m_ChannelName!)}");

            var response = await m_HttpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Failed to resolve Twitch broadcaster user ID ({(int)response.StatusCode}): {responseBody}");
            }

            var json = JObject.Parse(responseBody);
            return json["data"]?.FirstOrDefault()?["id"]?.ToObject<string>();
        }

        private void HandleNotification(JObject json)
        {
            var messageId = json["metadata"]?["message_id"]?.ToObject<string>();
            if (!string.IsNullOrWhiteSpace(messageId) && !TryTrackHandledMessageId(messageId))
                return;

            var subscriptionType = json["payload"]?["subscription"]?["type"]?.ToObject<string>();
            if (subscriptionType != "channel.channel_points_custom_reward_redemption.add")
                return;

            var rewardId = json["payload"]?["event"]?["reward"]?["id"]?.ToObject<string>();
            var rewardTitle = json["payload"]?["event"]?["reward"]?["title"]?.ToObject<string>();
            var redemptionId = json["payload"]?["event"]?["id"]?.ToObject<string>();
            var userName = json["payload"]?["event"]?["user_name"]?.ToObject<string>() ?? "unknown user";

            var mapping = FindMatchingMapping(rewardId, rewardTitle);
            if (mapping == null)
            {
                m_Logger.Information("Ignoring unmapped Twitch channel point reward \"{RewardTitle}\" from {UserName}", rewardTitle, userName);
                return;
            }

            _ = ProcessRewardRedemption(mapping, rewardId, rewardTitle, redemptionId, userName);
        }

        private async Task ProcessRewardRedemption(TwitchChannelPointRewardMapping mapping, string? rewardId, string? rewardTitle,
            string? redemptionId, string userName)
        {
            try
            {
                ChannelPointCommandResult commandResult;
                switch (mapping.Action)
                {
                case TwitchChannelPointActionType.TriggerEffect:
                    if (string.IsNullOrWhiteSpace(mapping.EffectId))
                    {
                        commandResult = new ChannelPointCommandResult(
                            ChannelPointCommandResolution.KnownFailure,
                            "No effect was configured for this reward.");
                    }
                    else
                    {
                        m_Logger.Information(
                            "Redeemer {UserName} requested effect {EffectId} using reward \"{RewardTitle}\"",
                            userName, mapping.EffectId, rewardTitle);
                        commandResult = await m_ChaosPipe.SendChannelPointCommandAsync("trigger_effect", mapping.EffectId);
                    }
                    break;
                case TwitchChannelPointActionType.ClearEffects:
                    m_Logger.Information("Redeemer {UserName} requested clear effects using reward \"{RewardTitle}\"",
                        userName, rewardTitle);
                    commandResult = await m_ChaosPipe.SendChannelPointCommandAsync("clear_effects");
                    break;
                default:
                    commandResult = new ChannelPointCommandResult(
                        ChannelPointCommandResolution.KnownFailure,
                        "Unknown channel point action type.");
                    break;
                }

                await TryResolveRedemption(rewardId, rewardTitle, redemptionId, userName, commandResult);
            }
            catch (Exception exception)
            {
                m_Logger.Warning(exception, "Failed while processing Twitch channel point reward \"{RewardTitle}\"", rewardTitle);
            }
        }

        private async Task TryResolveRedemption(string? rewardId, string? rewardTitle, string? redemptionId, string userName,
            ChannelPointCommandResult commandResult)
        {
            switch (commandResult.Resolution)
            {
            case ChannelPointCommandResolution.Success:
                if (string.IsNullOrWhiteSpace(rewardId) || string.IsNullOrWhiteSpace(redemptionId))
                {
                    m_Logger.Warning("Channel point reward \"{RewardTitle}\" succeeded for {UserName}, but the redemption could not be auto-completed because Twitch did not provide complete IDs.",
                        rewardTitle, userName);
                    return;
                }

                try
                {
                    await UpdateRedemptionStatus(rewardId, redemptionId, "FULFILLED");
                    m_Logger.Information("Marked reward \"{RewardTitle}\" as fulfilled for {UserName}", rewardTitle, userName);
                }
                catch (Exception exception)
                {
                    m_Logger.Warning(exception,
                        "Chaos Mod confirmed reward \"{RewardTitle}\" for {UserName}, but Twitch fulfillment failed. Leaving it for manual review.",
                        rewardTitle, userName);
                }
                break;
            case ChannelPointCommandResolution.KnownFailure:
                if (string.IsNullOrWhiteSpace(rewardId) || string.IsNullOrWhiteSpace(redemptionId))
                {
                    m_Logger.Warning("Channel point reward \"{RewardTitle}\" definitely failed for {UserName}, but the redemption could not be refunded automatically because Twitch did not provide complete IDs.",
                        rewardTitle, userName);
                    return;
                }

                try
                {
                    await UpdateRedemptionStatus(rewardId, redemptionId, "CANCELED");
                    m_Logger.Information("Refunded reward \"{RewardTitle}\" for {UserName}: {Reason}",
                        rewardTitle, userName, commandResult.Message ?? "no reason supplied");
                }
                catch (Exception exception)
                {
                    m_Logger.Warning(exception,
                        "Reward \"{RewardTitle}\" definitely failed for {UserName}, but Twitch refunding failed. Leaving it for manual review.",
                        rewardTitle, userName);
                }
                break;
            default:
                m_Logger.Warning(
                    "Leaving reward \"{RewardTitle}\" unresolved for manual review because the proxy could not confirm success or failure for {UserName}. {Reason}",
                    rewardTitle, userName, commandResult.Message ?? "No further details were provided.");
                break;
            }
        }

        private void HandleRevocation(JObject json)
        {
            var status = json["payload"]?["subscription"]?["status"]?.ToObject<string>() ?? "unknown";
            m_Logger.Error("Twitch channel points subscription was revoked: {Status}", status);
            m_ChaosPipe.SendErrorMessage("Twitch channel points authorization was revoked. Please verify the configured token and scopes.");
        }

        private TwitchChannelPointRewardMapping? FindMatchingMapping(string? rewardId, string? rewardTitle)
        {
            if (!string.IsNullOrWhiteSpace(rewardId) && m_MappingsByRewardId.TryGetValue(rewardId, out var rewardIdMapping))
                return rewardIdMapping;

            if (!string.IsNullOrWhiteSpace(rewardTitle))
            {
                foreach (var mapping in m_MappingsByTitle)
                {
                    if (string.Equals(mapping.RewardTitle, rewardTitle, StringComparison.OrdinalIgnoreCase))
                        return mapping;
                }
            }

            return null;
        }

        private bool TryTrackHandledMessageId(string messageId)
        {
            if (!m_HandledMessageIds.Add(messageId))
                return false;

            m_HandledMessageIdsQueue.Enqueue(messageId);
            while (m_HandledMessageIdsQueue.Count > MAX_HANDLED_MESSAGE_IDS)
            {
                var oldMessageId = m_HandledMessageIdsQueue.Dequeue();
                m_HandledMessageIds.Remove(oldMessageId);
            }

            return true;
        }

        private static string NormalizeOAuthToken(string oauth)
        {
            var trimmed = oauth.Trim();
            if (trimmed.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase))
                return trimmed[6..];
            if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return trimmed[7..];

            return trimmed;
        }
    }
}
