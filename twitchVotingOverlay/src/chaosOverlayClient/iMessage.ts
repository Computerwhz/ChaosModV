import { IChaosOverlayVoteOption } from './iVoteOption';

export interface IChaosOverlayClientMessage {
	retainInitialVotes: boolean;
	request: 'CREATE' | 'END' | 'NO_VOTING_ROUND' | 'STATUS' | 'UPDATE';
	totalVotes: number;
	voteOptions: IChaosOverlayVoteOption[];
	votingMode: 'MAJORITY' | 'PERCENTAGE';
	channelPointsEnabled?: boolean;
	channelPointsPaused?: boolean;
}
