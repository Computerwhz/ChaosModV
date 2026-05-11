namespace TwitchChatVotingProxy.OverlayServer
{
    interface IOverlayServer
    {
        /// <summary>
        /// Informs the overlay server that the voting has ended
        /// </summary>
        void EndVoting();
        /// <summary>
        /// Informs the overlay server about a new vote
        /// </summary>
        /// <param name="voteOptions">The new voting options</param>
        void NewVoting(List<IVoteOption> voteOptions);
        /// <summary>
        /// Informs the overlay about a no voting round
        /// </summary>
        void NoVotingRound();
        /// <summary>
        /// Informs the overlay about possible updates
        /// </summary>
        /// <param name="votes"></param>
        void UpdateVoting(List<IVoteOption> votes);
        /// <summary>
        /// Informs the overlay about the managed Twitch channel point status
        /// </summary>
        /// <param name="enabled">Whether channel points are enabled in config</param>
        /// <param name="paused">Whether the configured rewards are currently paused</param>
        void SetChannelPointsStatus(bool enabled, bool paused);
    }
}
