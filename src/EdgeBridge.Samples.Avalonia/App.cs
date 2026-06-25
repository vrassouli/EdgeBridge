using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace EdgeBridge.Samples.Avalonia;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(CreateApplicationStyles());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Styles CreateApplicationStyles()
    {
        return
        [
            new Style(x => x.OfType<Button>())
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, SolidColorBrush.Parse("#FFFFFF")),
                    new Setter(Button.ForegroundProperty, SolidColorBrush.Parse("#172033")),
                    new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse("#C9D2E0")),
                    new Setter(Button.PaddingProperty, new Thickness(14, 8)),
                    new Setter(Button.CornerRadiusProperty, new CornerRadius(6)),
                    new Setter(Button.FontWeightProperty, FontWeight.SemiBold)
                }
            },
            new Style(x => x.OfType<Button>().Class(":pointerover"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, SolidColorBrush.Parse("#EEF3FA")),
                    new Setter(Button.ForegroundProperty, SolidColorBrush.Parse("#172033")),
                    new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse("#AEBBD0"))
                }
            },
            new Style(x => x.OfType<Button>().Class(":pressed"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, SolidColorBrush.Parse("#E1E8F2")),
                    new Setter(Button.ForegroundProperty, SolidColorBrush.Parse("#172033")),
                    new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse("#97A8BF"))
                }
            },
            new Style(x => x.OfType<Button>().Class(":disabled"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, SolidColorBrush.Parse("#F2F5F9")),
                    new Setter(Button.ForegroundProperty, SolidColorBrush.Parse("#8B96A8")),
                    new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse("#D8DEE8"))
                }
            },
            new Style(x => x.OfType<Button>().Class("primary"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, SolidColorBrush.Parse("#2457D6")),
                    new Setter(Button.ForegroundProperty, Brushes.White),
                    new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse("#2457D6"))
                }
            },
            new Style(x => x.OfType<Button>().Class("primary").Class(":pointerover"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, SolidColorBrush.Parse("#1D49B8")),
                    new Setter(Button.ForegroundProperty, Brushes.White),
                    new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse("#1D49B8"))
                }
            },
            new Style(x => x.OfType<Button>().Class("primary").Class(":pressed"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, SolidColorBrush.Parse("#173A91")),
                    new Setter(Button.ForegroundProperty, Brushes.White),
                    new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse("#173A91"))
                }
            },
            new Style(x => x.OfType<Button>().Class("primary").Class(":disabled"))
            {
                Setters =
                {
                    new Setter(Button.BackgroundProperty, SolidColorBrush.Parse("#D7E1F8")),
                    new Setter(Button.ForegroundProperty, SolidColorBrush.Parse("#6F7C92")),
                    new Setter(Button.BorderBrushProperty, SolidColorBrush.Parse("#D7E1F8"))
                }
            },
            new Style(x => x.OfType<ListBox>())
            {
                Setters =
                {
                    new Setter(ListBox.PaddingProperty, new Thickness(0)),
                    new Setter(ListBox.BackgroundProperty, Brushes.Transparent),
                    new Setter(ListBox.BorderThicknessProperty, new Thickness(0))
                }
            },
            new Style(x => x.OfType<ListBoxItem>())
            {
                Setters =
                {
                    new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                    new Setter(ListBoxItem.MarginProperty, new Thickness(0, 0, 0, 10)),
                    new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                    new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent),
                    new Setter(ListBoxItem.ForegroundProperty, SolidColorBrush.Parse("#172033"))
                }
            },
            new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
            {
                Setters =
                {
                    new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent),
                    new Setter(ListBoxItem.ForegroundProperty, SolidColorBrush.Parse("#172033"))
                }
            },
            new Style(x => x.OfType<TextBox>())
            {
                Setters =
                {
                    new Setter(TextBox.BackgroundProperty, Brushes.White),
                    new Setter(TextBox.ForegroundProperty, SolidColorBrush.Parse("#172033")),
                    new Setter(TextBox.CornerRadiusProperty, new CornerRadius(6)),
                    new Setter(TextBox.BorderBrushProperty, SolidColorBrush.Parse("#C9D2E0")),
                    new Setter(TextBox.PaddingProperty, new Thickness(10, 7))
                }
            },
            new Style(x => x.OfType<NumericUpDown>())
            {
                Setters =
                {
                    new Setter(NumericUpDown.BackgroundProperty, Brushes.White),
                    new Setter(NumericUpDown.ForegroundProperty, SolidColorBrush.Parse("#172033")),
                    new Setter(NumericUpDown.CornerRadiusProperty, new CornerRadius(6)),
                    new Setter(NumericUpDown.BorderBrushProperty, SolidColorBrush.Parse("#C9D2E0"))
                }
            },
            new Style(x => x.OfType<Border>().Class("listCard").Class(":pointerover"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, SolidColorBrush.Parse("#EFF4FB")),
                    new Setter(Border.BorderBrushProperty, SolidColorBrush.Parse("#BCD0EA"))
                }
            },
            new Style(x => x.OfType<Border>().Class("navItem").Class(":pointerover"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, SolidColorBrush.Parse("#EFF4FB")),
                    new Setter(Border.BorderBrushProperty, SolidColorBrush.Parse("#BCD0EA"))
                }
            },
            new Style(x => x.OfType<TextBlock>().Class("pageTitle"))
            {
                Setters =
                {
                    new Setter(TextBlock.FontSizeProperty, 20d),
                    new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold),
                    new Setter(TextBlock.ForegroundProperty, SolidColorBrush.Parse("#172033"))
                }
            },
            new Style(x => x.OfType<TextBlock>().Class("fieldLabel"))
            {
                Setters =
                {
                    new Setter(TextBlock.FontSizeProperty, 12d),
                    new Setter(TextBlock.ForegroundProperty, SolidColorBrush.Parse("#647086")),
                    new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold)
                }
            },
            BorderStyle("itemCard", "#FFFFFF", "#D9E0EA", 8, new Thickness(14), new Thickness(0, 0, 0, 10)),
            BorderStyle("metricCard", "#FFFFFF", "#D9E0EA", 8, new Thickness(16), new Thickness(0, 0, 12, 12)),
            BorderStyle("listCard", "#F8FAFD", "#E1E7F0", 8, new Thickness(12), new Thickness(0)),
            BorderStyle("navItem", "#F8FAFD", "#E1E7F0", 6, new Thickness(12, 10), new Thickness(0)),
            BorderStyle("statePill", "#EAF1FF", "#BBD0FF", 999, new Thickness(14, 0), new Thickness(0)),
            BorderStyle("warningCard", "#FFF7E6", "#E8C56B", 8, new Thickness(12), new Thickness(0)),
            new Style(x => x.OfType<TextBlock>().Class("metricLabel"))
            {
                Setters =
                {
                    new Setter(TextBlock.ForegroundProperty, SolidColorBrush.Parse("#647086")),
                    new Setter(TextBlock.FontSizeProperty, 12d),
                    new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold)
                }
            },
            new Style(x => x.OfType<TextBlock>().Class("metricValue"))
            {
                Setters =
                {
                    new Setter(TextBlock.ForegroundProperty, SolidColorBrush.Parse("#172033")),
                    new Setter(TextBlock.FontSizeProperty, 17d),
                    new Setter(TextBlock.FontWeightProperty, FontWeight.SemiBold)
                }
            }
        ];
    }

    private static Style BorderStyle(
        string className,
        string background,
        string border,
        double radius,
        Thickness padding,
        Thickness margin)
    {
        return new Style(x => x.OfType<Border>().Class(className))
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, SolidColorBrush.Parse(background)),
                new Setter(Border.BorderBrushProperty, SolidColorBrush.Parse(border)),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(radius)),
                new Setter(Border.PaddingProperty, padding),
                new Setter(Border.MarginProperty, margin)
            }
        };
    }
}
