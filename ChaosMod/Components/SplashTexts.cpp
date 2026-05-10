#include <stdafx.h>

#include "SplashTexts.h"

#include "Info.h"

#include "Util/OptionsManager.h"

static bool ms_InitalSplashShown = false;

SplashTexts::SplashTexts()
{
	m_EnableSplashTexts =
	    g_OptionsManager.GetConfigValue({ "EnableModSplashTexts" }, OPTION_DEFAULT_ENABLE_SPLASH_TEXTS);
	m_EnableStartupSplashTexts =
	    g_OptionsManager.GetConfigValue({ "EnableStartupSplashTexts" }, OPTION_DEFAULT_ENABLE_SPLASH_TEXTS);
	m_EnableVotingSplashTexts =
	    g_OptionsManager.GetConfigValue({ "EnableVotingSplashTexts" }, OPTION_DEFAULT_ENABLE_SPLASH_TEXTS);
	m_EnableClearEffectsSplashTexts =
	    g_OptionsManager.GetConfigValue({ "EnableClearEffectsSplashTexts" }, OPTION_DEFAULT_ENABLE_SPLASH_TEXTS);
	m_EnableVotingProxySplashTexts =
	    g_OptionsManager.GetConfigValue({ "EnableVotingProxySplashTexts" }, OPTION_DEFAULT_ENABLE_SPLASH_TEXTS);

	if (ms_InitalSplashShown)
		return;

	ShowSplash("Chaos Mod v" MOD_VERSION "\n\nSee credits.txt for a list of contributors", { .2f, .3f }, .65f,
	           { 60, 245, 190 }, 10, SplashType::Startup);
#ifdef CHAOSDEBUG
	ShowSplash("DEBUG BUILD!", { .2f, .5f }, .7f, { 255, 0, 0 }, 10, SplashType::Startup);
#endif

	ms_InitalSplashShown = true;
}

void SplashTexts::OnModPauseCleanup(PauseCleanupFlags cleanupFlags)
{
	m_ActiveSplashes.clear();
}

void SplashTexts::OnRun()
{
	float frameTime = GET_FRAME_TIME();

	for (std::list<SplashText>::iterator it = m_ActiveSplashes.begin(); it != m_ActiveSplashes.end();)
	{
		DrawScreenText(it->Text, it->TextPos, it->Scale, it->TextColor, true);
		it->Time -= frameTime;

		if (it->Time <= 0)
			it = m_ActiveSplashes.erase(it);
		else
			it++;
	}
}

void SplashTexts::ShowSplash(const std::string &text, const ScreenTextVector &textPos, float scale,
                             Color textColor, std::uint8_t time, SplashType splashType)
{
	if (!IsSplashTypeEnabled(splashType))
		return;

	m_ActiveSplashes.emplace_back(text, textPos, scale, textColor, time);
}

void SplashTexts::ShowVotingSplash()
{
	ShowSplash("Voting Enabled!", { .86f, .7f }, .8f, { 255, 100, 100 }, 10, SplashType::Voting);
}

void SplashTexts::ShowClearEffectsSplash()
{
	ShowSplash("Effects Cleared!", { .86f, .86f }, .8f, { 255, 100, 100 }, 10, SplashType::ClearEffects);
}

void SplashTexts::ShowChannelPointsPauseStateSplash(bool paused)
{
	ShowSplash(paused ? "Channel Points Paused!" : "Channel Points Resumed!", { .86f, .82f }, .7f,
	           { 255, 100, 100 }, 8, SplashType::Voting);
}

void SplashTexts::ShowVotingProxyDisconnectedSplash()
{
	ShowSplash("Voting Proxy Disconnected!", { .86f, .78f }, .7f, { 255, 100, 100 }, 8, SplashType::VotingProxy);
}

void SplashTexts::ShowVotingProxyErrorSplash(const std::string &reason)
{
	std::string splashText = "Voting Proxy Error!";
	if (!reason.empty())
	{
		auto shortenedReason = reason;
		auto punctuationPos  = shortenedReason.find_first_of(".!?");
		if (punctuationPos != std::string::npos)
			shortenedReason = shortenedReason.substr(0, punctuationPos + 1);
		if (shortenedReason.length() > 56)
			shortenedReason = shortenedReason.substr(0, 53) + "...";

		splashText += "\n";
		splashText += shortenedReason;
	}

	ShowSplash(splashText, { .86f, .78f }, .55f, { 255, 100, 100 }, 8, SplashType::VotingProxy);
}

bool SplashTexts::IsSplashTypeEnabled(SplashType splashType) const
{
	if (!m_EnableSplashTexts)
		return false;

	switch (splashType)
	{
	case SplashType::Startup:
		return m_EnableStartupSplashTexts;
	case SplashType::Voting:
		return m_EnableVotingSplashTexts;
	case SplashType::ClearEffects:
		return m_EnableClearEffectsSplashTexts;
	case SplashType::VotingProxy:
		return m_EnableVotingProxySplashTexts;
	default:
		return true;
	}
}
