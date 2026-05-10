namespace Shared
{
    public enum TwitchChannelPointActionType
    {
        TriggerEffect,
        ClearEffects
    }

    public class TwitchChannelPointRewardMapping
    {
        public string? RewardId { get; set; } = null;
        public string? RewardTitle { get; set; } = null;
        public int RewardCost { get; set; } = 1000;
        public TwitchChannelPointActionType Action { get; set; } = TwitchChannelPointActionType.TriggerEffect;
        public string? EffectId { get; set; } = null;
    }
}
