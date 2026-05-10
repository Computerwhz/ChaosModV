using System.Windows;
using System.Windows.Controls;

namespace ConfigApp.Tabs.Settings
{
    public class GeneralTab : Tab
    {
        private CheckBox? m_DisableModOnStartup = null;
        private TextBox? m_MaxRunningEffects = null;
        private CheckBox? m_DisableDrawTimer = null;
        private CheckBox? m_DisableDrawEffectText = null;
        private TextBox? m_RandomSeed = null;
        private CheckBox? m_EnableEffectGroupWeighting = null;
        private CheckBox? m_EnableModSplashTexts = null;
        private CheckBox? m_EnableStartupSplashTexts = null;
        private CheckBox? m_EnableVotingSplashTexts = null;
        private CheckBox? m_EnableClearEffectsSplashTexts = null;
        private CheckBox? m_EnableVotingProxySplashTexts = null;
        private CheckBox? m_EnableFailsafe = null;

        private void UpdateSplashCheckboxesEnabledState()
        {
            var enabled = m_EnableModSplashTexts?.IsChecked.GetValueOrDefault() ?? false;

            if (m_EnableStartupSplashTexts is not null)
                m_EnableStartupSplashTexts.IsEnabled = enabled;
            if (m_EnableVotingSplashTexts is not null)
                m_EnableVotingSplashTexts.IsEnabled = enabled;
            if (m_EnableClearEffectsSplashTexts is not null)
                m_EnableClearEffectsSplashTexts.IsEnabled = enabled;
            if (m_EnableVotingProxySplashTexts is not null)
                m_EnableVotingProxySplashTexts.IsEnabled = enabled;
        }

        protected override void InitContent()
        {
            PushNewColumn(new GridLength(1f, GridUnitType.Star));
            SetRowHeight(new GridLength(1f, GridUnitType.Star));

            var scrollViewer = new ScrollViewer();

            var grid = new ChaosGrid();
            grid.PushNewColumn(new GridLength(310f));
            grid.PushNewColumn(new GridLength(10f));
            grid.PushNewColumn(new GridLength(100f));
            grid.PushNewColumn(new GridLength(450f));
            grid.PushNewColumn(new GridLength(10f));
            grid.PushNewColumn(new GridLength());

            grid.PushRowSpacedPair("Disable mod on startup by default", m_DisableModOnStartup = Utils.GenerateCommonCheckBox());
            grid.PushRowSpacedPair("Max amount of simultaneously running effects", m_MaxRunningEffects = Utils.GenerateCommonNumericOnlyTextBox(2));
            grid.PopRow();

            grid.PushRowSpacedPair("Don't draw timer bar", m_DisableDrawTimer = Utils.GenerateCommonCheckBox());
            grid.PushRowSpacedPair("Don't draw effect text", m_DisableDrawEffectText = Utils.GenerateCommonCheckBox());
            grid.PopRow();

            grid.PushRowSpacedPair("Random Seed (Leave empty for random seed every time)", m_RandomSeed = new TextBox()
            {
                Width = 200f,
                Height = 20f
            });
            grid.PushRowSpacedPair("Enable effect group weighting", m_EnableEffectGroupWeighting = Utils.GenerateCommonCheckBox());
            grid.PopRow();

            grid.PushRowSpacedPair("Show mod splash texts", m_EnableModSplashTexts = Utils.GenerateCommonCheckBox());
            m_EnableModSplashTexts.Click += (sender, eventArgs) => { UpdateSplashCheckboxesEnabledState(); };
            grid.PushRowSpacedPair("Allow prevention of repetitive mission fails (Failsafe)", m_EnableFailsafe = Utils.GenerateCommonCheckBox());
            grid.PopRow();

            grid.PushRowSpacedPair("Show startup splash texts", m_EnableStartupSplashTexts = Utils.GenerateCommonCheckBox());
            grid.PushRowSpacedPair("Show voting splash texts", m_EnableVotingSplashTexts = Utils.GenerateCommonCheckBox());
            grid.PopRow();

            grid.PushRowSpacedPair("Show clear-effects splash texts", m_EnableClearEffectsSplashTexts = Utils.GenerateCommonCheckBox());
            grid.PushRowSpacedPair("Show voting proxy splash texts", m_EnableVotingProxySplashTexts = Utils.GenerateCommonCheckBox());
            grid.PopRow();

            scrollViewer.Content = grid.Grid;

            PushRowElement(scrollViewer);
        }

        public override void OnLoadValues()
        {
            if (m_DisableDrawTimer is not null)
                m_DisableDrawTimer.IsChecked = OptionsManager.ConfigFile.ReadValue("DisableTimerBarDraw", false);
            if (m_DisableDrawEffectText is not null)
                m_DisableDrawEffectText.IsChecked = OptionsManager.ConfigFile.ReadValue("DisableEffectTextDraw", false);
            if (m_RandomSeed is not null)
                m_RandomSeed.Text = OptionsManager.ConfigFile.ReadValue<string>("Seed");
            if (m_MaxRunningEffects is not null)
                m_MaxRunningEffects.Text = $"{OptionsManager.ConfigFile.ReadValue("MaxParallelRunningEffects", 99)}";
            if (m_EnableEffectGroupWeighting is not null)
                m_EnableEffectGroupWeighting.IsChecked = OptionsManager.ConfigFile.ReadValue("EnableGroupWeightingAdjustments", true);
            if (m_DisableModOnStartup is not null)
                m_DisableModOnStartup.IsChecked = OptionsManager.ConfigFile.ReadValue("DisableStartup", false);
            if (m_EnableFailsafe is not null)
                m_EnableFailsafe.IsChecked = OptionsManager.ConfigFile.ReadValue("EnableFailsafe", true);
            if (m_EnableModSplashTexts is not null)
                m_EnableModSplashTexts.IsChecked = OptionsManager.ConfigFile.ReadValue("EnableModSplashTexts", true);
            if (m_EnableStartupSplashTexts is not null)
                m_EnableStartupSplashTexts.IsChecked = OptionsManager.ConfigFile.ReadValue("EnableStartupSplashTexts", true);
            if (m_EnableVotingSplashTexts is not null)
                m_EnableVotingSplashTexts.IsChecked = OptionsManager.ConfigFile.ReadValue("EnableVotingSplashTexts", true);
            if (m_EnableClearEffectsSplashTexts is not null)
                m_EnableClearEffectsSplashTexts.IsChecked = OptionsManager.ConfigFile.ReadValue("EnableClearEffectsSplashTexts", true);
            if (m_EnableVotingProxySplashTexts is not null)
                m_EnableVotingProxySplashTexts.IsChecked = OptionsManager.ConfigFile.ReadValue("EnableVotingProxySplashTexts", true);

            UpdateSplashCheckboxesEnabledState();
        }

        public override void OnSaveValues()
        {
            OptionsManager.ConfigFile.WriteValue("DisableTimerBarDraw", m_DisableDrawTimer?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("DisableEffectTextDraw", m_DisableDrawEffectText?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("Seed", m_RandomSeed?.Text);
            OptionsManager.ConfigFile.WriteValueAsInt("MaxParallelRunningEffects", m_MaxRunningEffects?.Text);
            OptionsManager.ConfigFile.WriteValue("EnableGroupWeightingAdjustments", m_EnableEffectGroupWeighting?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("DisableStartup", m_DisableModOnStartup?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("EnableFailsafe", m_EnableFailsafe?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("EnableModSplashTexts", m_EnableModSplashTexts?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("EnableStartupSplashTexts", m_EnableStartupSplashTexts?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("EnableVotingSplashTexts", m_EnableVotingSplashTexts?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("EnableClearEffectsSplashTexts", m_EnableClearEffectsSplashTexts?.IsChecked);
            OptionsManager.ConfigFile.WriteValue("EnableVotingProxySplashTexts", m_EnableVotingProxySplashTexts?.IsChecked);
        }
    }
}
