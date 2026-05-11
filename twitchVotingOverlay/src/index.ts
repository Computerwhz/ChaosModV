import { ChaosOverlayClient } from './chaosOverlayClient';
import { BarOverlay } from './barOverlay';

// Get DOM elements
const BAR_CONTAINER = document.getElementById('barContainer') as HTMLDivElement | null;
const CHANNEL_POINTS_STATUS_CONTAINER = document.getElementById('channelPointsStatusContainer') as HTMLDivElement | null;
const CHANNEL_POINTS_STATUS_DOT = document.getElementById('channelPointsStatusDot') as HTMLDivElement | null;
const TOTAL_VOTES = document.getElementById('totalVotes') as HTMLDivElement | null;

if (BAR_CONTAINER === null) throw new Error('could not find bar container in DOM');
if (CHANNEL_POINTS_STATUS_CONTAINER === null) throw new Error('could not find channel points status container in DOM');
if (CHANNEL_POINTS_STATUS_DOT === null) throw new Error('could not find channel points status dot in DOM');
if (TOTAL_VOTES === null) throw new Error('could not find total votes element in DOM');

const OVERLAY_CLIENT = new ChaosOverlayClient('ws://localhost:9091');
const ACTIVE_STATUS_CLASS = 'active';
const HIDDEN_STATUS_CLASS = 'hidden';
const PAUSED_STATUS_CLASS = 'paused';

const hideChannelPointsStatus = () => {
	CHANNEL_POINTS_STATUS_CONTAINER.classList.add(HIDDEN_STATUS_CLASS);
	CHANNEL_POINTS_STATUS_DOT.classList.remove(ACTIVE_STATUS_CLASS, PAUSED_STATUS_CLASS);
};

const showChannelPointsStatus = (paused: boolean) => {
	CHANNEL_POINTS_STATUS_CONTAINER.classList.remove(HIDDEN_STATUS_CLASS);
	CHANNEL_POINTS_STATUS_DOT.classList.toggle(PAUSED_STATUS_CLASS, paused);
	CHANNEL_POINTS_STATUS_DOT.classList.toggle(ACTIVE_STATUS_CLASS, !paused);
};

OVERLAY_CLIENT.addConnectListener(() => {
	TOTAL_VOTES.style.opacity = '1';
});
OVERLAY_CLIENT.addDisconnectListener(() => {
	TOTAL_VOTES.style.opacity = '0';
	hideChannelPointsStatus();
});
OVERLAY_CLIENT.addStatusListener(message => {
	if (!message.channelPointsEnabled) {
		hideChannelPointsStatus();
		return;
	}

	showChannelPointsStatus(message.channelPointsPaused ?? false);
});
OVERLAY_CLIENT.addUpdateVoteListener(message => {
	TOTAL_VOTES.innerText = `Total Votes: ${message.totalVotes}`;
});

new BarOverlay(BAR_CONTAINER, OVERLAY_CLIENT);
