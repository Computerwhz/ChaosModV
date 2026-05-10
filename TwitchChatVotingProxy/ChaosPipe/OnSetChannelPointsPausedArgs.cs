namespace TwitchChatVotingProxy.ChaosPipe
{
    class OnSetChannelPointsPausedArgs
    {
        public bool Paused { get; }

        public OnSetChannelPointsPausedArgs(bool paused)
        {
            Paused = paused;
        }
    }
}
