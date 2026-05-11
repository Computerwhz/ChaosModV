using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using Shared;

namespace ConfigApp.Tabs.Voting
{
    public class TwitchTab : Tab
    {
        private static readonly Brush ms_TableContainerBackground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF6));
        private static readonly Brush ms_TableContainerBorderBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0xCD, 0xD6));
        private static readonly Brush ms_TableHeaderBackground = new SolidColorBrush(Color.FromRgb(0xE3, 0xE7, 0xED));
        private static readonly Brush ms_TableRowBackgroundEven = new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFC));
        private static readonly Brush ms_TableRowBackgroundOdd = new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF7));
        private static readonly Brush ms_TableControlBackground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5));
        private static readonly Brush ms_TableControlBackgroundDisabled = new SolidColorBrush(Color.FromRgb(0xD9, 0xDC, 0xE1));
        private static readonly Brush ms_TableControlBorderBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x72, 0x7D));
        private static readonly Brush ms_TableControlForeground = Brushes.Black;
        private static readonly Brush ms_TableControlForegroundDisabled = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x40));
        private static readonly Brush ms_TableButtonBackground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5));
        private static readonly Brush ms_TableButtonBackgroundDisabled = new SolidColorBrush(Color.FromRgb(0xD9, 0xDC, 0xE1));
        private static readonly Brush ms_TableButtonForegroundDisabled = new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x68));

        private sealed class ChannelPointEffectOption
        {
            public string? EffectId { get; set; } = null;
            public string DisplayLabel { get; set; } = string.Empty;
        }

        private sealed class ChannelPointActionOption
        {
            public TwitchChannelPointActionType Action { get; set; }
            public string DisplayLabel { get; set; } = string.Empty;
        }

        private sealed class ChannelPointRewardRow
        {
            public string? RewardId { get; set; } = null;
            public string? RewardTitle { get; set; } = null;
            public int RewardCost { get; set; } = 1000;
            public TwitchChannelPointActionType Action { get; set; } = TwitchChannelPointActionType.TriggerEffect;
            public string? EffectId { get; set; } = null;
            public bool UseCooldownAndLimits { get; set; } = false;
            public bool IsCooldownAndLimitsExpanded { get; set; } = false;
            public int GlobalCooldownMinutes { get; set; } = 0;
            public int MaxRedemptionsPerStream { get; set; } = 0;
            public int MaxRedemptionsPerUserPerStream { get; set; } = 0;
        }

        private static readonly IReadOnlyList<ChannelPointActionOption> ms_ActionOptions = new[]
        {
            new ChannelPointActionOption()
            {
                Action = TwitchChannelPointActionType.TriggerEffect,
                DisplayLabel = "Trigger Effect"
            },
            new ChannelPointActionOption()
            {
                Action = TwitchChannelPointActionType.ClearEffects,
                DisplayLabel = "Clear All Active Effects"
            }
        };

        private static readonly IReadOnlyList<ChannelPointEffectOption> ms_EffectOptions = new[]
        {
            new ChannelPointEffectOption()
            {
                EffectId = null,
                DisplayLabel = "(Not Used)"
            }
        }.Concat(Effects.EffectsMap
            .OrderBy(entry => entry.Value.Name)
            .Select(entry => new ChannelPointEffectOption()
            {
                EffectId = entry.Key,
                DisplayLabel = $"{entry.Value.Name} [{entry.Key}]"
            }))
            .ToList();

        private CheckBox? m_EnableTwitchVoting = null;
        private TextBox? m_ChannelName = null;
        private TextBox? m_UserName = null;
        private PasswordBox? m_Token = null;

        private CheckBox? m_EnableTwitchChannelPoints = null;
        private TextBox? m_ChannelPointsChannelName = null;
        private TextBox? m_ChannelPointsClientId = null;
        private PasswordBox? m_ChannelPointsToken = null;
        private TextBox? m_ChannelPointsRewardDescription = null;
        private StackPanel? m_ChannelPointMappingsPanel = null;

        private readonly List<ChannelPointRewardRow> m_ChannelPointRewardRows = new();
        private bool m_IsSyncingChannelNameFields = false;

        private void UpdateElementsEnabledState()
        {
            var votingEnabled = m_EnableTwitchVoting?.IsChecked.GetValueOrDefault() ?? false;
            var channelPointsEnabled = m_EnableTwitchChannelPoints?.IsChecked.GetValueOrDefault() ?? false;

            if (m_ChannelName is not null)
                m_ChannelName.IsEnabled = votingEnabled || channelPointsEnabled;
            if (m_UserName is not null)
                m_UserName.IsEnabled = votingEnabled;
            if (m_Token is not null)
                m_Token.IsEnabled = votingEnabled;

            if (m_ChannelPointsClientId is not null)
                m_ChannelPointsClientId.IsEnabled = channelPointsEnabled;
            if (m_ChannelPointsToken is not null)
                m_ChannelPointsToken.IsEnabled = channelPointsEnabled;
            if (m_ChannelPointsChannelName is not null)
                m_ChannelPointsChannelName.IsEnabled = channelPointsEnabled;
            if (m_ChannelPointsRewardDescription is not null)
                m_ChannelPointsRewardDescription.IsEnabled = channelPointsEnabled;
            if (m_ChannelPointMappingsPanel is not null)
                m_ChannelPointMappingsPanel.IsEnabled = channelPointsEnabled;
        }

        private void SyncSharedChannelName(TextBox? sourceTextBox, TextBox? targetTextBox)
        {
            if (sourceTextBox == null || targetTextBox == null || m_IsSyncingChannelNameFields)
                return;

            m_IsSyncingChannelNameFields = true;
            targetTextBox.Text = sourceTextBox.Text;
            m_IsSyncingChannelNameFields = false;
        }

        private static Border WrapSection(string title, UIElement body, Brush? background = null)
        {
            var container = new StackPanel();
            container.Children.Add(new TextBlock()
            {
                Text = title,
                FontSize = 14f,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0f, 0f, 0f, 10f)
            });
            container.Children.Add(body);

            return new Border()
            {
                Background = background ?? new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF6)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0xD3, 0xD3)),
                BorderThickness = new Thickness(1f),
                CornerRadius = new CornerRadius(4f),
                Padding = new Thickness(14f),
                Margin = new Thickness(0f, 0f, 0f, 12f),
                Child = container
            };
        }

        private static TextBlock CreateTableHeaderText(string text)
        {
            return new TextBlock()
            {
                Text = text,
                Foreground = Brushes.Black,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8f, 0f, 8f, 0f)
            };
        }

        private static TextBlock CreateInfoText(string text)
        {
            return new TextBlock()
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0f, 0f, 0f, 10f)
            };
        }

        private static Style CreateDarkComboBoxItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, ms_TableControlBackground));
            style.Setters.Add(new Setter(Control.ForegroundProperty, ms_TableControlForeground));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, ms_TableControlBorderBrush));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6f, 3f, 6f, 3f)));

            var highlightedTrigger = new Trigger()
            {
                Property = ComboBoxItem.IsHighlightedProperty,
                Value = true
            };
            highlightedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xD6, 0xDE, 0xE9))));
            style.Triggers.Add(highlightedTrigger);

            var selectedTrigger = new Trigger()
            {
                Property = ComboBoxItem.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xC4, 0xD0, 0xDF))));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        private static void ApplyDarkComboBoxStyle(ComboBox comboBox)
        {
            comboBox.Background = ms_TableControlBackground;
            comboBox.Foreground = ms_TableControlForeground;
            comboBox.BorderBrush = ms_TableControlBorderBrush;
            comboBox.BorderThickness = new Thickness(1f);
            comboBox.Padding = new Thickness(6f, 2f, 6f, 2f);
            comboBox.SnapsToDevicePixels = true;
            comboBox.UseLayoutRounding = true;
            comboBox.ItemContainerStyle = CreateDarkComboBoxItemStyle();
            comboBox.Resources[SystemColors.WindowBrushKey] = ms_TableControlBackground;
            comboBox.Resources[SystemColors.ControlTextBrushKey] = ms_TableControlForeground;
            comboBox.Resources[SystemColors.HighlightBrushKey] = new SolidColorBrush(Color.FromRgb(0xD6, 0xDE, 0xE9));
            comboBox.Resources[SystemColors.HighlightTextBrushKey] = ms_TableControlForeground;
        }

        private static void ApplyDarkButtonStyle(Button button)
        {
            button.Background = ms_TableButtonBackground;
            button.Foreground = ms_TableControlForeground;
            button.BorderBrush = ms_TableControlBorderBrush;
            button.BorderThickness = new Thickness(1f);
            button.SnapsToDevicePixels = true;
            button.UseLayoutRounding = true;
            button.Padding = new Thickness(8f, 2f, 8f, 2f);

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, ms_TableButtonBackground));
            style.Setters.Add(new Setter(Control.ForegroundProperty, ms_TableControlForeground));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, ms_TableControlBorderBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1f)));

            var disabledTrigger = new Trigger()
            {
                Property = UIElement.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(Control.BackgroundProperty, ms_TableButtonBackgroundDisabled));
            disabledTrigger.Setters.Add(new Setter(Control.ForegroundProperty, ms_TableButtonForegroundDisabled));
            style.Triggers.Add(disabledTrigger);

            button.Style = style;
        }

        private static void ApplyTableTextBoxStyle(TextBox textBox, bool isEnabled = true)
        {
            textBox.Background = isEnabled ? ms_TableControlBackground : ms_TableControlBackgroundDisabled;
            textBox.Foreground = isEnabled ? ms_TableControlForeground : ms_TableControlForegroundDisabled;
            textBox.BorderBrush = ms_TableControlBorderBrush;
            textBox.BorderThickness = new Thickness(1f);
            textBox.SnapsToDevicePixels = true;
            textBox.UseLayoutRounding = true;
            textBox.IsEnabled = isEnabled;
        }

        private static int ConvertCooldownSecondsToMinutes(int seconds)
        {
            if (seconds <= 0)
                return 0;

            return Math.Max(1, (int)Math.Ceiling(seconds / 60d));
        }

        private static int ConvertCooldownMinutesToSeconds(int minutes)
        {
            if (minutes <= 0)
                return 0;

            return Math.Clamp(minutes * 60, 60, 604800);
        }

        private static string GetGeneratedManagedRewardTitle(ChannelPointRewardRow row)
        {
            if (row.Action == TwitchChannelPointActionType.ClearEffects)
                return "Chaos Mod: Clear All Active Effects";

            if (!string.IsNullOrWhiteSpace(row.EffectId)
                && Effects.EffectsMap.TryGetValue(row.EffectId, out var effectInfo)
                && !string.IsNullOrWhiteSpace(effectInfo.Name))
            {
                return $"Chaos Mod: {effectInfo.Name}";
            }

            return "Chaos Mod: Trigger Effect";
        }

        private static string GetSavedManagedRewardTitle(ChannelPointRewardRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.RewardTitle))
                return row.RewardTitle.Trim();

            return GetGeneratedManagedRewardTitle(row);
        }

        private static void UpdateRewardTitleIfUsingGeneratedValue(ChannelPointRewardRow row, string previousGeneratedTitle)
        {
            if (string.IsNullOrWhiteSpace(row.RewardTitle)
                || string.Equals(row.RewardTitle.Trim(), previousGeneratedTitle, StringComparison.Ordinal))
            {
                row.RewardTitle = GetGeneratedManagedRewardTitle(row);
            }
        }

        private void AddMappingRow()
        {
            var row = new ChannelPointRewardRow()
            {
                RewardCost = 1000,
                Action = TwitchChannelPointActionType.TriggerEffect,
                EffectId = ms_EffectOptions.FirstOrDefault(option => option.EffectId != null)?.EffectId
            };
            row.RewardTitle = GetGeneratedManagedRewardTitle(row);
            m_ChannelPointRewardRows.Add(row);
            RenderChannelPointMappings();
        }

        private void RenderChannelPointMappings()
        {
            if (m_ChannelPointMappingsPanel == null)
                return;

            m_ChannelPointMappingsPanel.Children.Clear();

            var headerRow = new Grid()
            {
                Background = ms_TableHeaderBackground,
                Height = 34f
            };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(110f) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(220f) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(170f) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1f, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(88f) });

            var pointsHeader = CreateTableHeaderText("Points");
            pointsHeader.SetValue(Grid.ColumnProperty, 0);
            headerRow.Children.Add(pointsHeader);

            var rewardNameHeader = CreateTableHeaderText("Reward Name");
            rewardNameHeader.SetValue(Grid.ColumnProperty, 1);
            headerRow.Children.Add(rewardNameHeader);

            var actionHeader = CreateTableHeaderText("Action");
            actionHeader.SetValue(Grid.ColumnProperty, 2);
            headerRow.Children.Add(actionHeader);

            var effectHeader = CreateTableHeaderText("Effect");
            effectHeader.SetValue(Grid.ColumnProperty, 3);
            headerRow.Children.Add(effectHeader);

            m_ChannelPointMappingsPanel.Children.Add(headerRow);

            for (var index = 0; index < m_ChannelPointRewardRows.Count; index++)
            {
                var row = m_ChannelPointRewardRows[index];
                var rowBackground = index % 2 == 0 ? ms_TableRowBackgroundEven : ms_TableRowBackgroundOdd;

                var rowContainer = new StackPanel()
                {
                    Background = rowBackground,
                    Margin = new Thickness(0f, 1f, 0f, 0f)
                };

                var rowGrid = new Grid()
                {
                    Background = rowBackground,
                    Height = 42f,
                    SnapsToDevicePixels = true,
                    UseLayoutRounding = true
                };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(110f) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(220f) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(170f) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1f, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(88f) });

                var pointsBox = Utils.GenerateCommonNumericOnlyTextBox(7, 100f, 28f);
                pointsBox.Margin = new Thickness(8f, 6f, 8f, 6f);
                pointsBox.Text = $"{row.RewardCost}";
                ApplyTableTextBoxStyle(pointsBox);
                pointsBox.TextChanged += (sender, eventArgs) =>
                {
                    row.RewardCost = int.TryParse(pointsBox.Text, out var cost) && cost > 0 ? cost : 0;
                };
                pointsBox.SetValue(Grid.ColumnProperty, 0);
                rowGrid.Children.Add(pointsBox);

                var rewardTitleBox = new TextBox()
                {
                    Margin = new Thickness(8f, 6f, 8f, 6f),
                    Height = 28f,
                    Text = GetSavedManagedRewardTitle(row)
                };
                ApplyTableTextBoxStyle(rewardTitleBox);
                rewardTitleBox.TextChanged += (sender, eventArgs) =>
                {
                    row.RewardTitle = rewardTitleBox.Text;
                };
                rewardTitleBox.SetValue(Grid.ColumnProperty, 1);
                rowGrid.Children.Add(rewardTitleBox);

                var actionCombo = new ComboBox()
                {
                    Margin = new Thickness(8f, 6f, 8f, 6f),
                    Height = 28f,
                    ItemsSource = ms_ActionOptions,
                    DisplayMemberPath = nameof(ChannelPointActionOption.DisplayLabel),
                    SelectedItem = ms_ActionOptions.First(option => option.Action == row.Action)
                };
                ApplyDarkComboBoxStyle(actionCombo);
                actionCombo.SelectionChanged += (sender, eventArgs) =>
                {
                    if (actionCombo.SelectedItem is ChannelPointActionOption selectedAction)
                    {
                        var previousGeneratedTitle = GetGeneratedManagedRewardTitle(row);
                        row.Action = selectedAction.Action;
                        if (row.Action == TwitchChannelPointActionType.ClearEffects)
                            row.EffectId = null;
                        else if (string.IsNullOrWhiteSpace(row.EffectId))
                            row.EffectId = ms_EffectOptions.FirstOrDefault(option => option.EffectId != null)?.EffectId;

                        UpdateRewardTitleIfUsingGeneratedValue(row, previousGeneratedTitle);
                        RenderChannelPointMappings();
                    }
                };
                actionCombo.SetValue(Grid.ColumnProperty, 2);
                rowGrid.Children.Add(actionCombo);

                var effectCombo = new ComboBox()
                {
                    Margin = new Thickness(8f, 6f, 8f, 6f),
                    Height = 28f,
                    ItemsSource = ms_EffectOptions,
                    DisplayMemberPath = nameof(ChannelPointEffectOption.DisplayLabel),
                    IsHitTestVisible = row.Action == TwitchChannelPointActionType.TriggerEffect,
                    Focusable = row.Action == TwitchChannelPointActionType.TriggerEffect,
                    SelectedItem = row.Action == TwitchChannelPointActionType.TriggerEffect
                        ? ms_EffectOptions.FirstOrDefault(option => option.EffectId == row.EffectId) ?? ms_EffectOptions[0]
                        : ms_EffectOptions[0]
                };
                ApplyDarkComboBoxStyle(effectCombo);
                effectCombo.Background = row.Action == TwitchChannelPointActionType.TriggerEffect
                    ? ms_TableControlBackground
                    : ms_TableControlBackgroundDisabled;
                effectCombo.Foreground = row.Action == TwitchChannelPointActionType.TriggerEffect
                    ? ms_TableControlForeground
                    : ms_TableControlForegroundDisabled;
                effectCombo.SelectionChanged += (sender, eventArgs) =>
                {
                    if (effectCombo.SelectedItem is ChannelPointEffectOption selectedEffect)
                    {
                        var previousGeneratedTitle = GetGeneratedManagedRewardTitle(row);
                        row.EffectId = selectedEffect.EffectId;
                        UpdateRewardTitleIfUsingGeneratedValue(row, previousGeneratedTitle);
                        RenderChannelPointMappings();
                    }
                };
                effectCombo.SetValue(Grid.ColumnProperty, 3);
                rowGrid.Children.Add(effectCombo);

                var removeButton = new Button()
                {
                    Content = "Remove",
                    Margin = new Thickness(6f, 6f, 6f, 6f),
                    Height = 28f
                };
                ApplyDarkButtonStyle(removeButton);
                removeButton.Click += (sender, eventArgs) =>
                {
                    m_ChannelPointRewardRows.Remove(row);
                    RenderChannelPointMappings();
                };
                removeButton.SetValue(Grid.ColumnProperty, 4);
                rowGrid.Children.Add(removeButton);

                rowContainer.Children.Add(rowGrid);

                var limitsGrid = new Grid()
                {
                    Margin = new Thickness(12f, 2f, 12f, 10f)
                };
                limitsGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(220f) });
                limitsGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(150f) });
                limitsGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1f, GridUnitType.Star) });
                for (var rowIndex = 0; rowIndex < 4; rowIndex++)
                    limitsGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

                var limitsToggle = new CheckBox()
                {
                    Content = "Enabled",
                    IsChecked = row.UseCooldownAndLimits,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8f, 0f, 0f, 0f)
                };

                var limitsExpanderHeader = new DockPanel()
                {
                    LastChildFill = false
                };
                var limitsHeaderText = new TextBlock()
                {
                    Text = "Cooldown & Limits",
                    Foreground = ms_TableControlForeground,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(limitsHeaderText, Dock.Left);
                limitsExpanderHeader.Children.Add(limitsHeaderText);
                DockPanel.SetDock(limitsToggle, Dock.Right);
                limitsExpanderHeader.Children.Add(limitsToggle);

                var limitsHelp = new TextBlock()
                {
                    Text = "Set any numeric field to 0 to leave that specific Twitch limit disabled. Cooldown is stored in whole minutes.",
                    Foreground = ms_TableControlForegroundDisabled,
                    Margin = new Thickness(0f, 0f, 0f, 8f),
                    TextWrapping = TextWrapping.Wrap
                };
                limitsHelp.SetValue(Grid.RowProperty, 0);
                limitsHelp.SetValue(Grid.ColumnSpanProperty, 3);
                limitsGrid.Children.Add(limitsHelp);

                var cooldownLabel = new TextBlock()
                {
                    Text = "Redemption Cooldown (Minutes)",
                    Foreground = ms_TableControlForeground,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0f, 0f, 12f, 8f)
                };
                cooldownLabel.SetValue(Grid.RowProperty, 1);
                cooldownLabel.SetValue(Grid.ColumnProperty, 0);
                limitsGrid.Children.Add(cooldownLabel);

                var cooldownBox = Utils.GenerateCommonNumericOnlyTextBox(5, 140f, 28f);
                cooldownBox.Margin = new Thickness(0f, 0f, 8f, 8f);
                cooldownBox.Text = row.GlobalCooldownMinutes > 0 ? $"{row.GlobalCooldownMinutes}" : "0";
                cooldownBox.TextChanged += (sender, eventArgs) =>
                {
                    row.GlobalCooldownMinutes = int.TryParse(cooldownBox.Text, out var minutes) && minutes > 0 ? minutes : 0;
                };
                cooldownBox.SetValue(Grid.RowProperty, 1);
                cooldownBox.SetValue(Grid.ColumnProperty, 1);
                limitsGrid.Children.Add(cooldownBox);

                var cooldownHint = new TextBlock()
                {
                    Text = "Up to 10080 minutes (7 days).",
                    Foreground = ms_TableControlForegroundDisabled,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0f, 0f, 0f, 8f)
                };
                cooldownHint.SetValue(Grid.RowProperty, 1);
                cooldownHint.SetValue(Grid.ColumnProperty, 2);
                limitsGrid.Children.Add(cooldownHint);

                var maxPerStreamLabel = new TextBlock()
                {
                    Text = "Limit Redemptions Per Stream",
                    Foreground = ms_TableControlForeground,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0f, 0f, 12f, 8f)
                };
                maxPerStreamLabel.SetValue(Grid.RowProperty, 2);
                maxPerStreamLabel.SetValue(Grid.ColumnProperty, 0);
                limitsGrid.Children.Add(maxPerStreamLabel);

                var maxPerStreamBox = Utils.GenerateCommonNumericOnlyTextBox(6, 140f, 28f);
                maxPerStreamBox.Margin = new Thickness(0f, 0f, 8f, 8f);
                maxPerStreamBox.Text = row.MaxRedemptionsPerStream > 0 ? $"{row.MaxRedemptionsPerStream}" : "0";
                maxPerStreamBox.TextChanged += (sender, eventArgs) =>
                {
                    row.MaxRedemptionsPerStream = int.TryParse(maxPerStreamBox.Text, out var maxPerStream) && maxPerStream > 0
                        ? maxPerStream
                        : 0;
                };
                maxPerStreamBox.SetValue(Grid.RowProperty, 2);
                maxPerStreamBox.SetValue(Grid.ColumnProperty, 1);
                limitsGrid.Children.Add(maxPerStreamBox);

                var maxPerUserLabel = new TextBlock()
                {
                    Text = "Limit Redemptions Per User / Stream",
                    Foreground = ms_TableControlForeground,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0f, 0f, 12f, 0f)
                };
                maxPerUserLabel.SetValue(Grid.RowProperty, 3);
                maxPerUserLabel.SetValue(Grid.ColumnProperty, 0);
                limitsGrid.Children.Add(maxPerUserLabel);

                var maxPerUserBox = Utils.GenerateCommonNumericOnlyTextBox(6, 140f, 28f);
                maxPerUserBox.Margin = new Thickness(0f, 0f, 8f, 0f);
                maxPerUserBox.Text = row.MaxRedemptionsPerUserPerStream > 0 ? $"{row.MaxRedemptionsPerUserPerStream}" : "0";
                maxPerUserBox.TextChanged += (sender, eventArgs) =>
                {
                    row.MaxRedemptionsPerUserPerStream = int.TryParse(maxPerUserBox.Text, out var maxPerUser) && maxPerUser > 0
                        ? maxPerUser
                        : 0;
                };
                maxPerUserBox.SetValue(Grid.RowProperty, 3);
                maxPerUserBox.SetValue(Grid.ColumnProperty, 1);
                limitsGrid.Children.Add(maxPerUserBox);

                void UpdateCooldownAndLimitControls()
                {
                    row.UseCooldownAndLimits = limitsToggle.IsChecked == true;

                    ApplyTableTextBoxStyle(cooldownBox, row.UseCooldownAndLimits);
                    ApplyTableTextBoxStyle(maxPerStreamBox, row.UseCooldownAndLimits);
                    ApplyTableTextBoxStyle(maxPerUserBox, row.UseCooldownAndLimits);

                    var labelOpacity = row.UseCooldownAndLimits ? 1f : 0.65f;
                    cooldownLabel.Opacity = labelOpacity;
                    cooldownHint.Opacity = labelOpacity;
                    maxPerStreamLabel.Opacity = labelOpacity;
                    maxPerUserLabel.Opacity = labelOpacity;
                    limitsHelp.Opacity = row.UseCooldownAndLimits ? 1f : 0.75f;
                }

                limitsToggle.Checked += (sender, eventArgs) => { UpdateCooldownAndLimitControls(); };
                limitsToggle.Unchecked += (sender, eventArgs) => { UpdateCooldownAndLimitControls(); };
                UpdateCooldownAndLimitControls();

                var limitsExpander = new Expander()
                {
                    Header = limitsExpanderHeader,
                    Content = limitsGrid,
                    IsExpanded = row.IsCooldownAndLimitsExpanded,
                    Margin = new Thickness(12f, 0f, 12f, 10f),
                    Background = rowBackground
                };
                limitsExpander.Expanded += (sender, eventArgs) =>
                {
                    foreach (var otherRow in m_ChannelPointRewardRows)
                        otherRow.IsCooldownAndLimitsExpanded = ReferenceEquals(otherRow, row);

                    RenderChannelPointMappings();
                };
                limitsExpander.Collapsed += (sender, eventArgs) => { row.IsCooldownAndLimitsExpanded = false; };

                rowContainer.Children.Add(limitsExpander);
                m_ChannelPointMappingsPanel.Children.Add(rowContainer);
            }

            var footerRow = new DockPanel()
            {
                Margin = new Thickness(0f, 10f, 0f, 0f)
            };
            var addButton = new Button()
            {
                Content = "Add",
                Width = 80f,
                Height = 28f,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ApplyDarkButtonStyle(addButton);
            addButton.Click += (sender, eventArgs) => { AddMappingRow(); };
            footerRow.Children.Add(addButton);

            m_ChannelPointMappingsPanel.Children.Add(footerRow);
        }

        protected override void InitContent()
        {
            PushNewColumn(new GridLength(1f, GridUnitType.Star));
            SetRowHeight(new GridLength(1f, GridUnitType.Star));

            var scrollViewer = new ScrollViewer()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var rootPanel = new StackPanel()
            {
                Margin = new Thickness(10f)
            };
            scrollViewer.Content = rootPanel;

            rootPanel.Children.Add(CreateInfoText("Configure Twitch chat voting and Twitch channel point rewards here. "
                + "Managed Chaos Mod rewards are created and updated automatically by the Twitch proxy."));

            var chatVotingGrid = new ChaosGrid();
            chatVotingGrid.PushNewColumn(new GridLength(220f));
            chatVotingGrid.PushNewColumn(new GridLength(10f));
            chatVotingGrid.PushNewColumn(new GridLength(180f));
            chatVotingGrid.PushNewColumn(new GridLength(1f, GridUnitType.Star));

            m_EnableTwitchVoting = new CheckBox()
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Content = "Enable Twitch Voting"
            };
            m_EnableTwitchVoting.Click += (sender, eventArgs) => { UpdateElementsEnabledState(); };
            chatVotingGrid.PushRowSpacedPair("Chat Voting", m_EnableTwitchVoting);
            chatVotingGrid.PopRow();

            chatVotingGrid.PushRowSpacedPair("Channel Name", m_ChannelName = new TextBox()
            {
                Width = 180f,
                Height = 22f
            });
            m_ChannelName.TextChanged += (sender, eventArgs) =>
            {
                SyncSharedChannelName(m_ChannelName, m_ChannelPointsChannelName);
            };
            chatVotingGrid.PopRow();
            chatVotingGrid.PushRowSpacedPair("Username", m_UserName = new TextBox()
            {
                Width = 180f,
                Height = 22f
            });
            chatVotingGrid.PopRow();

            chatVotingGrid.PushRowSpacedPair("OAuth Token", m_Token = new PasswordBox()
            {
                Width = 180f,
                Height = 22f
            });

            rootPanel.Children.Add(WrapSection("Chat Voting", chatVotingGrid.Grid));

            var channelPointsPanel = new StackPanel();
            channelPointsPanel.Children.Add(CreateInfoText("Define managed Chaos Mod rewards here. "
                + "Each row becomes a Twitch reward owned by the proxy, which allows it to pause rewards when the mod is inactive and unpause them when the mod is running. "
                + "Queued redemptions are only completed or refunded automatically when Chaos Mod can confirm the outcome."));

            var channelPointsHeaderGrid = new ChaosGrid();
            channelPointsHeaderGrid.PushNewColumn(new GridLength(220f));
            channelPointsHeaderGrid.PushNewColumn(new GridLength(10f));
            channelPointsHeaderGrid.PushNewColumn(new GridLength(520f));
            channelPointsHeaderGrid.PushNewColumn(new GridLength(1f, GridUnitType.Star));

            m_EnableTwitchChannelPoints = new CheckBox()
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Content = "Enable Twitch Channel Points"
            };
            m_EnableTwitchChannelPoints.Click += (sender, eventArgs) => { UpdateElementsEnabledState(); };
            channelPointsHeaderGrid.PushRowSpacedPair("Channel Points", m_EnableTwitchChannelPoints);
            channelPointsHeaderGrid.PopRow();

            channelPointsHeaderGrid.PushRowSpacedPair("Channel Name", m_ChannelPointsChannelName = new TextBox()
            {
                Width = 180f,
                Height = 22f
            }, "The Twitch channel that owns these managed rewards.");
            m_ChannelPointsChannelName.TextChanged += (sender, eventArgs) =>
            {
                SyncSharedChannelName(m_ChannelPointsChannelName, m_ChannelName);
            };
            channelPointsHeaderGrid.PopRow();

            channelPointsHeaderGrid.PushRowSpacedPair("Client ID", m_ChannelPointsClientId = new TextBox()
            {
                Width = 180f,
                Height = 22f
            }, "Your Twitch application client ID.");
            channelPointsHeaderGrid.PopRow();

            channelPointsHeaderGrid.PushRowSpacedPair("OAuth Token", m_ChannelPointsToken = new PasswordBox()
            {
                Width = 180f,
                Height = 22f
            }, "A Twitch user access token with channel:manage:redemptions.");
            channelPointsHeaderGrid.PopRow();

            channelPointsHeaderGrid.PushRowSpacedPair("Reward Description", m_ChannelPointsRewardDescription = new TextBox()
            {
                Width = 520f,
                Height = 22f
            }, "Shared Twitch reward description used for all Chaos effect rewards.");

            channelPointsPanel.Children.Add(channelPointsHeaderGrid.Grid);

            m_ChannelPointMappingsPanel = new StackPanel()
            {
                Margin = new Thickness(0f, 10f, 0f, 0f)
            };
            channelPointsPanel.Children.Add(new Border()
            {
                Background = ms_TableContainerBackground,
                BorderBrush = ms_TableContainerBorderBrush,
                BorderThickness = new Thickness(1f),
                CornerRadius = new CornerRadius(4f),
                Padding = new Thickness(10f),
                Child = m_ChannelPointMappingsPanel
            });

            rootPanel.Children.Add(WrapSection("Channel Points", channelPointsPanel));

            PushRowElement(scrollViewer);

            RenderChannelPointMappings();
            UpdateElementsEnabledState();
        }

        public override void OnLoadValues()
        {
            if (m_EnableTwitchVoting is not null)
                m_EnableTwitchVoting.IsChecked = OptionsManager.VotingFile.ReadValue("EnableVotingTwitch", false);
            if (m_ChannelName is not null)
                m_ChannelName.Text = OptionsManager.VotingFile.ReadValue<string>("TwitchChannelName");
            if (m_UserName is not null)
                m_UserName.Text = OptionsManager.VotingFile.ReadValue<string>("TwitchUserName");
            if (m_Token is not null)
                m_Token.Password = OptionsManager.VotingFile.ReadValue<string>("TwitchChannelOAuth");

            if (m_EnableTwitchChannelPoints is not null)
                m_EnableTwitchChannelPoints.IsChecked = OptionsManager.VotingFile.ReadValue("EnableTwitchChannelPoints", false);
            if (m_ChannelPointsChannelName is not null)
                m_ChannelPointsChannelName.Text = OptionsManager.VotingFile.ReadValue<string>("TwitchChannelName");
            if (m_ChannelPointsClientId is not null)
                m_ChannelPointsClientId.Text = OptionsManager.VotingFile.ReadValue<string>("TwitchChannelPointsClientId");
            if (m_ChannelPointsToken is not null)
                m_ChannelPointsToken.Password = OptionsManager.VotingFile.ReadValue<string>("TwitchChannelPointsOAuth");
            if (m_ChannelPointsRewardDescription is not null)
                m_ChannelPointsRewardDescription.Text = OptionsManager.VotingFile.ReadValue("TwitchChannelPointsRewardDescription",
                    "Immediately invoke this effect in the Chaos Mod.");

            m_ChannelPointRewardRows.Clear();

            var mappings = OptionsManager.VotingFile.ReadValue("TwitchChannelPointRewardMappings", new List<TwitchChannelPointRewardMapping>())
                ?? new List<TwitchChannelPointRewardMapping>();
            foreach (var mapping in mappings)
            {
                m_ChannelPointRewardRows.Add(new ChannelPointRewardRow()
                {
                    RewardId = mapping.RewardId,
                    RewardTitle = string.IsNullOrWhiteSpace(mapping.RewardTitle) ? null : mapping.RewardTitle,
                    RewardCost = mapping.RewardCost > 0 ? mapping.RewardCost : 1000,
                    Action = mapping.Action,
                    EffectId = mapping.EffectId,
                    UseCooldownAndLimits = mapping.UseCooldownAndLimits,
                    GlobalCooldownMinutes = ConvertCooldownSecondsToMinutes(mapping.GlobalCooldownSeconds),
                    MaxRedemptionsPerStream = Math.Max(mapping.MaxRedemptionsPerStream, 0),
                    MaxRedemptionsPerUserPerStream = Math.Max(mapping.MaxRedemptionsPerUserPerStream, 0)
                });

                var addedRow = m_ChannelPointRewardRows[^1];
                if (string.IsNullOrWhiteSpace(addedRow.RewardTitle))
                    addedRow.RewardTitle = GetGeneratedManagedRewardTitle(addedRow);
            }

            if (m_ChannelPointRewardRows.Count == 0)
                AddMappingRow();
            else
                RenderChannelPointMappings();

            UpdateElementsEnabledState();
        }

        public override void OnSaveValues()
        {
            OptionsManager.VotingFile.WriteValue("EnableVotingTwitch", m_EnableTwitchVoting?.IsChecked);
            OptionsManager.VotingFile.WriteValue("TwitchChannelName",
                !string.IsNullOrWhiteSpace(m_ChannelPointsChannelName?.Text) ? m_ChannelPointsChannelName?.Text : m_ChannelName?.Text);
            OptionsManager.VotingFile.WriteValue("TwitchUserName", m_UserName?.Text);
            OptionsManager.VotingFile.WriteValue("TwitchChannelOAuth", m_Token?.Password);

            OptionsManager.VotingFile.WriteValue("EnableTwitchChannelPoints", m_EnableTwitchChannelPoints?.IsChecked);
            OptionsManager.VotingFile.WriteValue("TwitchChannelPointsClientId", m_ChannelPointsClientId?.Text);
            OptionsManager.VotingFile.WriteValue("TwitchChannelPointsOAuth", m_ChannelPointsToken?.Password);
            OptionsManager.VotingFile.WriteValue("TwitchChannelPointsRewardDescription", m_ChannelPointsRewardDescription?.Text);

            var mappings = m_ChannelPointRewardRows
                .Where(row => row.RewardCost > 0)
                .Select(row => new TwitchChannelPointRewardMapping()
                {
                    RewardId = row.RewardId,
                    RewardTitle = GetSavedManagedRewardTitle(row),
                    RewardCost = row.RewardCost,
                    Action = row.Action,
                    EffectId = row.Action == TwitchChannelPointActionType.TriggerEffect ? row.EffectId : null,
                    UseCooldownAndLimits = row.UseCooldownAndLimits,
                    GlobalCooldownSeconds = ConvertCooldownMinutesToSeconds(row.GlobalCooldownMinutes),
                    MaxRedemptionsPerStream = Math.Max(row.MaxRedemptionsPerStream, 0),
                    MaxRedemptionsPerUserPerStream = Math.Max(row.MaxRedemptionsPerUserPerStream, 0)
                })
                .ToList();
            OptionsManager.VotingFile.WriteValue("TwitchChannelPointRewardMappings", JArray.FromObject(mappings));
        }
    }
}
