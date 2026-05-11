(() => {
	'use strict';

	class LiteEvent {
		constructor() {
			this.handlers = [];
		}

		addEventListener(handler) {
			this.handlers.push(handler);
		}

		removeEventListener(handler) {
			this.handlers = this.handlers.filter(existingHandler => existingHandler !== handler);
		}

		dispatch(data) {
			this.handlers.slice(0).forEach(handler => handler(data));
		}
	}

	class ChaosOverlayClient {
		static RECONNECT_INTERVAL = 1000;

		constructor(url) {
			this.URL = url;
			this.WS = null;
			this.createEvent = new LiteEvent();
			this.connectEvent = new LiteEvent();
			this.disconnectEvent = new LiteEvent();
			this.endEvent = new LiteEvent();
			this.noVoteRoundEvent = new LiteEvent();
			this.statusEvent = new LiteEvent();
			this.updateEvent = new LiteEvent();

			this.onSocketClose = this.onSocketClose.bind(this);
			this.onSocketError = this.onSocketError.bind(this);
			this.onSocketMessage = this.onSocketMessage.bind(this);
			this.onSocketOpen = this.onSocketOpen.bind(this);

			this.connect();
		}

		addCreateVoteListener(listener) {
			this.createEvent.addEventListener(listener);
		}

		addConnectListener(listener) {
			this.connectEvent.addEventListener(listener);
		}

		addDisconnectListener(listener) {
			this.disconnectEvent.addEventListener(listener);
		}

		addEndVoteListener(listener) {
			this.endEvent.addEventListener(listener);
		}

		addNoVotingRoundListener(listener) {
			this.noVoteRoundEvent.addEventListener(listener);
		}

		addStatusListener(listener) {
			this.statusEvent.addEventListener(listener);
		}

		addUpdateVoteListener(listener) {
			this.updateEvent.addEventListener(listener);
		}

		removeCreateVoteListener(listener) {
			this.createEvent.removeEventListener(listener);
		}

		removeConnectListener(listener) {
			this.connectEvent.removeEventListener(listener);
		}

		removeOnDisconnectListener(listener) {
			this.disconnectEvent.removeEventListener(listener);
		}

		removeEndVoteListener(listener) {
			this.endEvent.removeEventListener(listener);
		}

		removeNoVotingRoundListener(listener) {
			this.noVoteRoundEvent.removeEventListener(listener);
		}

		removeStatusListener(listener) {
			this.statusEvent.removeEventListener(listener);
		}

		removeUpdateVoteListener(listener) {
			this.updateEvent.removeEventListener(listener);
		}

		connect() {
			try {
				this.WS = new WebSocket(this.URL);
				this.WS.addEventListener('close', this.onSocketClose);
				this.WS.addEventListener('error', this.onSocketError);
				this.WS.addEventListener('message', this.onSocketMessage);
				this.WS.addEventListener('open', this.onSocketOpen);
			} catch (error) {
				// Connection errors are handled by the socket error callback.
			}
		}

		onSocketError(error) {
			console.log(`error in socket occurred: ${error.message}. closing socket`);
			if (this.WS !== null) this.WS.close();
		}

		onSocketClose() {
			console.log(`socket closed, reconnecting in ${ChaosOverlayClient.RECONNECT_INTERVAL}ms`);
			this.disconnectEvent.dispatch(null);
			window.setTimeout(() => this.connect(), ChaosOverlayClient.RECONNECT_INTERVAL);
		}

		onSocketMessage(message) {
			try {
				const parsedMessage = JSON.parse(message.data);
				if (typeof parsedMessage.channelPointsEnabled === 'boolean')
					this.statusEvent.dispatch(parsedMessage);

				switch (parsedMessage.request) {
					case 'CREATE':
						this.createEvent.dispatch(parsedMessage);
						break;
					case 'END':
						this.endEvent.dispatch(parsedMessage);
						break;
					case 'NO_VOTING_ROUND':
						this.noVoteRoundEvent.dispatch(parsedMessage);
						break;
					case 'STATUS':
						break;
					case 'UPDATE':
						this.updateEvent.dispatch(parsedMessage);
						break;
					default:
						console.warn(`unknown request type: ${parsedMessage.request}`);
				}
			} catch (error) {
				console.error(`failed to parse json data: ${error}`);
			}
		}

		onSocketOpen() {
			console.log('successfully connected to websocket');
			this.connectEvent.dispatch(null);
		}
	}

	const ANIMATION_DELAY_DELTA = 100;
	const ANIMATION_LENGTH = 600;

	class Bar {
		constructor(container) {
			this.container = container;

			this.bar = document.createElement('div');
			this.barProgression = document.createElement('div');
			this.labelContainer = document.createElement('div');
			this.labelLabel = document.createElement('span');
			this.labelMatch = document.createElement('span');
			this.labelValue = document.createElement('span');

			this.bar.classList.add('bar');
			this.barProgression.classList.add('progression');
			this.labelContainer.classList.add('labelContainer');

			this.labelContainer.append(this.labelMatch, this.labelLabel, this.labelValue);

			this.bar.append(this.barProgression);
			this.bar.append(this.labelContainer);
			this.container.append(this.bar);
		}

		set isDisabled(value) {
			const className = 'disabled';
			if (value) {
				this.bar.classList.add(className);
				this.barProgression.classList.add(className);
				this.labelContainer.classList.add(className);
			} else {
				this.bar.classList.remove(className);
				this.labelContainer.classList.remove(className);
				this.barProgression.classList.remove(className);
			}
		}

		set label(value) {
			this.labelLabel.innerText = value;
		}

		set match(value) {
			this.labelMatch.innerText = value;
		}

		set value(value) {
			this.labelValue.innerText = value;
		}

		set width(value) {
			this.barProgression.style.width = value;
		}

		fadeIn(duration, delay = 0) {
			this.bar.style.animationDelay = `${delay}ms`;
			this.bar.style.animationDuration = `${duration}ms`;
			this.bar.classList.add('slideIn');
			this.bar.classList.remove('slideOut');
		}

		fadeOut(duration, delay = 0) {
			this.bar.style.animationDelay = `${delay}ms`;
			this.bar.style.animationDuration = `${duration}ms`;
			this.bar.classList.add('slideOut');
			this.bar.classList.remove('slideIn');
		}
	}

	class BarOverlay {
		constructor(container, overlayClient) {
			this.container = container;
			this.bars = [];

			this.onCreateVote = this.onCreateVote.bind(this);
			this.onDisconnect = this.onDisconnect.bind(this);
			this.onEndVote = this.onEndVote.bind(this);
			this.onUpdateVote = this.onUpdateVote.bind(this);

			overlayClient.addCreateVoteListener(this.onCreateVote);
			overlayClient.addDisconnectListener(this.onDisconnect);
			overlayClient.addEndVoteListener(this.onEndVote);
			overlayClient.addUpdateVoteListener(this.onUpdateVote);
		}

		onCreateVote() {
			this.bars.forEach((bar, index) => {
				const animationDelay = index * ANIMATION_DELAY_DELTA;
				bar.fadeOut(ANIMATION_LENGTH, animationDelay);
				setTimeout(() => {
					bar.isDisabled = false;
					bar.fadeIn(ANIMATION_LENGTH);
				}, ANIMATION_LENGTH + animationDelay);
			});
		}

		onDisconnect() {
			this.bars.forEach((bar, index) => {
				const animationDelay = index * ANIMATION_DELAY_DELTA;
				bar.fadeOut(ANIMATION_LENGTH, animationDelay);
			});
		}

		onEndVote() {
			this.bars.forEach(bar => {
				bar.isDisabled = true;
			});
		}

		onUpdateVote(message) {
			const { retainInitialVotes, voteOptions, votingMode } = message;
			let { totalVotes } = message;

			if (votingMode === 'PERCENTAGE' && (totalVotes === 0 || retainInitialVotes)) {
				totalVotes += voteOptions.length;
				voteOptions.forEach(voteOption => {
					voteOption.value++;
				});
			}

			if (voteOptions.length !== this.bars.length) {
				while (this.container.firstChild) this.container.removeChild(this.container.firstChild);
				this.bars = voteOptions.map(() => new Bar(this.container));
			}

			for (let index = 0; index < voteOptions.length; index++) {
				const bar = this.bars[index];
				const voteOption = voteOptions[index];

				if (bar.isDisabled) continue;

				let percentage = 0;
				if (voteOption.value !== 0)
					percentage = Math.floor((voteOption.value / totalVotes) * 100);

				bar.label = voteOption.label;
				bar.match = voteOption.matches.join('/').concat('.');

				if (votingMode === 'MAJORITY') bar.value = voteOption.value.toString();
				else if (votingMode === 'PERCENTAGE') bar.value = `${percentage}%`;

				bar.width = `${percentage}%`;
			}
		}
	}

	const barContainer = document.getElementById('barContainer');
	const channelPointsStatusContainer = document.getElementById('channelPointsStatusContainer');
	const channelPointsStatusDot = document.getElementById('channelPointsStatusDot');
	const totalVotes = document.getElementById('totalVotes');

	if (barContainer === null) throw new Error('could not find bar container in DOM');
	if (channelPointsStatusContainer === null) throw new Error('could not find channel points status container in DOM');
	if (channelPointsStatusDot === null) throw new Error('could not find channel points status dot in DOM');
	if (totalVotes === null) throw new Error('could not find total votes element in DOM');

	const overlayClient = new ChaosOverlayClient('ws://localhost:9091');
	const ACTIVE_STATUS_CLASS = 'active';
	const HIDDEN_STATUS_CLASS = 'hidden';
	const PAUSED_STATUS_CLASS = 'paused';

	const hideChannelPointsStatus = () => {
		channelPointsStatusContainer.classList.add(HIDDEN_STATUS_CLASS);
		channelPointsStatusDot.classList.remove(ACTIVE_STATUS_CLASS, PAUSED_STATUS_CLASS);
	};

	const showChannelPointsStatus = paused => {
		channelPointsStatusContainer.classList.remove(HIDDEN_STATUS_CLASS);
		channelPointsStatusDot.classList.toggle(PAUSED_STATUS_CLASS, paused);
		channelPointsStatusDot.classList.toggle(ACTIVE_STATUS_CLASS, !paused);
	};

	overlayClient.addConnectListener(() => {
		totalVotes.style.opacity = '1';
	});
	overlayClient.addDisconnectListener(() => {
		totalVotes.style.opacity = '0';
		hideChannelPointsStatus();
	});
	overlayClient.addStatusListener(message => {
		if (!message.channelPointsEnabled) {
			hideChannelPointsStatus();
			return;
		}

		showChannelPointsStatus(message.channelPointsPaused ?? false);
	});
	overlayClient.addUpdateVoteListener(message => {
		totalVotes.innerText = `Total Votes: ${message.totalVotes}`;
	});

	new BarOverlay(barContainer, overlayClient);
})();
