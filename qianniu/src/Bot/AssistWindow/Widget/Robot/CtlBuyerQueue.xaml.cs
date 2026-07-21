using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bot.AssistWindow.Widget.Robot
{
    /// <summary>
    /// 同时接待多个买家时，每个买家最新一轮问答的数据。
    /// </summary>
    public class BuyerQueueItem
    {
        public string SellerNick { get; set; }
        public string BuyerNick { get; set; }
        public string Question { get; set; }
        public string Answer { get; set; }
        public bool IsProcessing { get; set; }
        public bool IsAutoReply { get; set; }
        public bool IsExpanded { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<KeyValuePair<string, string>> PriorRounds { get; set; }

        public BuyerQueueItem()
        {
            PriorRounds = new List<KeyValuePair<string, string>>();
        }
    }

    /// <summary>
    /// 多买家最新一轮对话汇总面板。详见 docs/adr/0003-multi-buyer-queue-panel.md。
    /// </summary>
    public partial class CtlBuyerQueue : UserControl
    {
        private const int MaxPriorRounds = 5;
        private readonly ConcurrentDictionary<string, BuyerQueueItem> _items =
            new ConcurrentDictionary<string, BuyerQueueItem>();

        public event Action<BuyerQueueItem> BuyerConfirmed;

        public CtlBuyerQueue()
        {
            InitializeComponent();
        }

        public void MarkReplying(string sellerNick, string buyerNick)
        {
            var item = _items.GetOrAdd(buyerNick, key => new BuyerQueueItem
            {
                SellerNick = sellerNick,
                BuyerNick = buyerNick
            });
            item.SellerNick = sellerNick;
            item.IsProcessing = true;
            item.UpdatedAt = DateTime.Now;
            Render();
        }

        public void UpdateAnswer(string sellerNick, string buyerNick, string question, string answer, bool isAutoReply)
        {
            var item = _items.GetOrAdd(buyerNick, key => new BuyerQueueItem
            {
                SellerNick = sellerNick,
                BuyerNick = buyerNick
            });

            if (!string.IsNullOrEmpty(item.Question) || !string.IsNullOrEmpty(item.Answer))
            {
                item.PriorRounds.Insert(0, new KeyValuePair<string, string>(item.Question, item.Answer));
                while (item.PriorRounds.Count > MaxPriorRounds)
                {
                    item.PriorRounds.RemoveAt(item.PriorRounds.Count - 1);
                }
            }

            item.SellerNick = sellerNick;
            item.Question = question;
            item.Answer = answer;
            item.IsAutoReply = isAutoReply;
            item.IsProcessing = false;
            item.UpdatedAt = DateTime.Now;
            Render();
        }

        public void RemoveBuyer(string buyerNick)
        {
            BuyerQueueItem removed;
            _items.TryRemove(buyerNick, out removed);
            Render();
        }

        private void Render()
        {
            stkQueue.Children.Clear();
            var ordered = _items.Values.OrderByDescending(i => i.UpdatedAt).ToList();
            foreach (var item in ordered)
            {
                stkQueue.Children.Add(BuildRow(item));
            }
            grdRoot.Visibility = ordered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Border BuildRow(BuyerQueueItem item)
        {
            var root = new Border
            {
                Background = (Brush)FindResource("uiSurfaceBrush"),
                BorderBrush = (Brush)FindResource("uiBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("uiRadiusSm"),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();

            var summaryGrid = new Grid { Margin = new Thickness(8, 6, 8, 6) };
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textPanel = new StackPanel();
            var buyerText = new TextBlock
            {
                Text = item.BuyerNick,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = (Brush)FindResource("uiTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var previewText = new TextBlock
            {
                Text = item.IsProcessing ? "回复中..." : CombinePreview(item.Question, item.Answer),
                FontSize = 11,
                Foreground = (Brush)FindResource(item.IsProcessing ? "uiAccentBrush" : "uiMutedTextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            };
            textPanel.Children.Add(buyerText);
            textPanel.Children.Add(previewText);
            Grid.SetColumn(textPanel, 0);
            summaryGrid.Children.Add(textPanel);

            var confirmButton = new Button
            {
                Content = "确认",
                Padding = new Thickness(10, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = !item.IsProcessing
            };
            confirmButton.Click += (s, e) =>
            {
                e.Handled = true;
                if (BuyerConfirmed != null)
                {
                    BuyerConfirmed(item);
                }
            };
            Grid.SetColumn(confirmButton, 1);
            summaryGrid.Children.Add(confirmButton);

            stack.Children.Add(summaryGrid);

            if (item.IsExpanded && item.PriorRounds.Count > 0)
            {
                var historyPanel = new StackPanel { Margin = new Thickness(8, 0, 8, 6) };
                foreach (var round in item.PriorRounds)
                {
                    historyPanel.Children.Add(new TextBlock
                    {
                        Text = CombinePreview(round.Key, round.Value),
                        FontSize = 11,
                        Foreground = (Brush)FindResource("uiSubtleTextBrush"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 4)
                    });
                }
                stack.Children.Add(historyPanel);
            }

            root.Child = stack;
            root.MouseLeftButtonUp += (s, e) =>
            {
                item.IsExpanded = !item.IsExpanded;
                Render();
            };
            return root;
        }

        private static string CombinePreview(string question, string answer)
        {
            return string.Format("问：{0}  答：{1}", question ?? string.Empty, answer ?? string.Empty);
        }
    }
}
