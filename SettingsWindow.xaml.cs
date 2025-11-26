using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TaskbarLyrics.Models;

namespace TaskbarLyrics
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrentConfig();
            
            this.MouseDown += SettingsWindow_MouseDown;
        }

        private void SettingsWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void LoadCurrentConfig()
        {
            var config = ConfigManager.CurrentConfig;

            foreach (ComboBoxItem item in FontFamilyComboBox.Items)
            {
                if (item.Content.ToString() == config.FontFamily)
                {
                    FontFamilyComboBox.SelectedItem = item;
                    break;
                }
            }

            foreach (ComboBoxItem item in FontSizeComboBox.Items)
            {
                if (item.Content.ToString() == config.FontSize.ToString())
                {
                    FontSizeComboBox.SelectedItem = item;
                    break;
                }
            }

            SelectColorInComboBox(FontColorComboBox, config.FontColor);
            
            SelectColorInComboBox(HighlightColorComboBox, config.HighlightColor);
            
            foreach (ComboBoxItem item in AlignmentComboBox.Items)
            {
                if (item.Tag.ToString() == config.Alignment.ToLower())
                {
                    AlignmentComboBox.SelectedItem = item;
                    break;
                }
            }

            SelectColorInComboBox(BackgroundColorComboBox, config.BackgroundColor);
            
            ShowTranslationCheckBox.IsChecked = config.ShowTranslation;
            
            foreach (ComboBoxItem item in TranslationFontSizeComboBox.Items)
            {
                if (item.Content.ToString() == config.TranslationFontSize.ToString())
                {
                    TranslationFontSizeComboBox.SelectedItem = item;
                    break;
                }
            }

            SelectColorInComboBox(TranslationColorComboBox, config.TranslationFontColor);
        }

        private void SelectColorInComboBox(ComboBox comboBox, string colorValue)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Tag.ToString() == colorValue)
                {
                    comboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void ApplyConfig()
        {
            var config = ConfigManager.CurrentConfig;

            if (FontFamilyComboBox.SelectedItem is ComboBoxItem fontFamilyItem)
            {
                config.FontFamily = fontFamilyItem.Content.ToString();
            }
            
            if (FontSizeComboBox.SelectedItem is ComboBoxItem fontSizeItem)
            {
                config.FontSize = int.Parse(fontSizeItem.Content.ToString());
            }

            if (FontColorComboBox.SelectedItem is ComboBoxItem fontColorItem)
            {
                config.FontColor = fontColorItem.Tag.ToString();
            }

            if (HighlightColorComboBox.SelectedItem is ComboBoxItem highlightColorItem)
            {
                config.HighlightColor = highlightColorItem.Tag.ToString();
            }

            if (AlignmentComboBox.SelectedItem is ComboBoxItem alignmentItem)
            {
                config.Alignment = alignmentItem.Tag.ToString();
            }

            if (BackgroundColorComboBox.SelectedItem is ComboBoxItem backgroundColorItem)
            {
                config.BackgroundColor = backgroundColorItem.Tag.ToString();
            }

            config.ShowTranslation = ShowTranslationCheckBox.IsChecked ?? true;

            if (TranslationFontSizeComboBox.SelectedItem is ComboBoxItem translationSizeItem)
            {
                config.TranslationFontSize = int.Parse(translationSizeItem.Content.ToString());
            }

            if (TranslationColorComboBox.SelectedItem is ComboBoxItem translationColorItem)
            {
                config.TranslationFontColor = translationColorItem.Tag.ToString();
            }

            ConfigManager.SaveConfig();

            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.RefreshConfig();
        }

        private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontFamilyComboBox.SelectedItem is ComboBoxItem item)
            {
                PreviewText.FontFamily = new FontFamily(item.Content.ToString());
            }
        }

        private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontSizeComboBox.SelectedItem is ComboBoxItem item)
            {
                PreviewText.FontSize = int.Parse(item.Content.ToString());
            }
        }

        private void FontColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontColorComboBox.SelectedItem is ComboBoxItem item)
            {
                var brush = new BrushConverter().ConvertFromString(item.Tag.ToString()) as Brush;
                PreviewText.Foreground = brush;
            }
        }

        private void HighlightColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void AlignmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AlignmentComboBox.SelectedItem is ComboBoxItem item)
            {
                var alignment = item.Tag.ToString();
                var horizontalAlignment = alignment == "left" ? HorizontalAlignment.Left : 
                                         alignment == "right" ? HorizontalAlignment.Right : 
                                         HorizontalAlignment.Center;
                PreviewText.HorizontalAlignment = horizontalAlignment;
                PreviewTranslation.HorizontalAlignment = horizontalAlignment;
            }
        }

        private void BackgroundColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackgroundColorComboBox.SelectedItem is ComboBoxItem item)
            {
                var brush = new BrushConverter().ConvertFromString(item.Tag.ToString()) as Brush;
                PreviewBorder.Background = brush;
            }
        }

        private void ShowTranslationCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            PreviewTranslation.Visibility = ShowTranslationCheckBox.IsChecked == true ? 
                Visibility.Visible : Visibility.Collapsed;
        }

        private void TranslationFontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TranslationFontSizeComboBox.SelectedItem is ComboBoxItem item)
            {
                PreviewTranslation.FontSize = int.Parse(item.Content.ToString());
            }
        }

        private void TranslationColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TranslationColorComboBox.SelectedItem is ComboBoxItem item)
            {
                var brush = new BrushConverter().ConvertFromString(item.Tag.ToString()) as Brush;
                PreviewTranslation.Foreground = brush;
            }
        }

        private void CustomFontColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorDialog();
            if (color != null)
            {
                ConfigManager.CurrentConfig.FontColor = color;
                ApplyConfig();
                LoadCurrentConfig();
            }
        }

        private void CustomHighlightColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorDialog();
            if (color != null)
            {
                ConfigManager.CurrentConfig.HighlightColor = color;
                ApplyConfig();
                LoadCurrentConfig();
            }
        }

        private void CustomBackgroundColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorDialog();
            if (color != null)
            {
                ConfigManager.CurrentConfig.BackgroundColor = color;
                ApplyConfig();
                LoadCurrentConfig();
            }
        }

        private void CustomTranslationColor_Click(object sender, RoutedEventArgs e)
        {
            var color = ShowColorDialog();
            if (color != null)
            {
                ConfigManager.CurrentConfig.TranslationFontColor = color;
                ApplyConfig();
                LoadCurrentConfig();
            }
        }

        private string ShowColorDialog()
        {
            var dialog = new System.Windows.Forms.ColorDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = dialog.Color;
                return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            return null;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyConfig();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyConfig();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            
            var scaleTransform = new ScaleTransform(1, 1);
            this.RenderTransform = scaleTransform;
            
            var animation = new System.Windows.Media.Animation.DoubleAnimation(0.95, 1, 
                new Duration(TimeSpan.FromMilliseconds(200)));
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }
}