﻿namespace Xam.Forms.Markdown
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Extensions;
    using Markdig.Syntax;
    using Markdig.Syntax.Inlines;
    using Xamarin.Forms;

    public class MarkdownView : ContentView
    {
        public Action<string> NavigateToLink { get; set; } = (s) => Device.OpenUri(new Uri(s));

        public static MarkdownTheme Global = new LightMarkdownTheme();

        public string Markdown
        {
            get { return (string)GetValue(MarkdownProperty); }
            set { SetValue(MarkdownProperty, value); }
        }

        public static readonly BindableProperty MarkdownProperty = BindableProperty.Create(nameof(Markdown), typeof(string), typeof(MarkdownView), null, propertyChanged: OnMarkdownChanged);

        public string RelativeUrlHost
        {
            get { return (string)GetValue(RelativeUrlHostProperty); }
            set { SetValue(RelativeUrlHostProperty, value); }
        }

        public static readonly BindableProperty RelativeUrlHostProperty = BindableProperty.Create(nameof(RelativeUrlHost), typeof(string), typeof(MarkdownView), null, propertyChanged: OnMarkdownChanged);

        public MarkdownTheme Theme
        {
            get { return (MarkdownTheme)GetValue(ThemeProperty); }
            set { SetValue(ThemeProperty, value); }
        }

        public static readonly BindableProperty ThemeProperty = BindableProperty.Create(nameof(Theme), typeof(MarkdownTheme), typeof(MarkdownView), Global, propertyChanged: OnMarkdownChanged);

        bool isQuoted;

        List<View> queuedViews = new List<View>();

        static void OnMarkdownChanged(BindableObject bindable, object oldValue, object newValue)
        {
            var view = bindable as MarkdownView;
            view.RenderMarkdown();
        }

        StackLayout stack;

        List<KeyValuePair<string, string>> links = new List<KeyValuePair<string, string>>();

        void RenderMarkdown()
        {
            stack = new StackLayout()
            {
                Spacing = Theme.Margin,
            };

            Padding = Theme.Margin;

            BackgroundColor = Theme.BackgroundColor;

            if (!string.IsNullOrEmpty(Markdown))
            {
                var parsed = Markdig.Markdown.Parse(Markdown);
                Render(parsed.AsEnumerable());
            }

            Content = stack;
        }

        void Render(IEnumerable<Block> blocks)
        {
            foreach (var block in blocks)
            {
                Render(block);
            }
        }

        void AttachLinks(View view)
        {
            if (links.Any())
            {
                var blockLinks = links;
                view.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(async () =>
                    {
                        try
                        {
                            if (blockLinks.Count > 1)
                            {
                                var result = await Application.Current.MainPage.DisplayActionSheet("Open link", "Cancel", null, blockLinks.Select(x => x.Key).ToArray());
                                var link = blockLinks.FirstOrDefault(x => x.Key == result);
                                NavigateToLink(link.Value);
                            }
                            else
                            {
                                NavigateToLink(blockLinks.First().Value);
                            }
                        }
                        catch (Exception) { }
                    }),
                });

                links = new List<KeyValuePair<string, string>>();
            }
        }

        #region Rendering blocks

        void Render(Block block)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    Render(heading);
                    break;

                case ParagraphBlock paragraph:
                    Render(paragraph);
                    break;

                case QuoteBlock quote:
                    Render(quote);
                    break;

                case CodeBlock code:
                    Render(code);
                    break;

                case ListBlock list:
                    Render(list);
                    break;

                case ThematicBreakBlock thematicBreak:
                    Render(thematicBreak);
                    break;

                case HtmlBlock html:
                    Render(html);
                    break;

                default:
                    Debug.WriteLine($"Can't render {block.GetType()} blocks.");
                    break;
            }

            if (queuedViews.Any())
            {
                foreach (var view in queuedViews)
                {
                    stack.Children.Add(view);
                }
                queuedViews.Clear();
            }
        }

        int listScope;

        void Render(ThematicBreakBlock block)
        {
            var style = Theme.Separator;

            if (style.BorderSize > 0)
            {
                stack.Children.Add(new BoxView
                {
                    HeightRequest = style.BorderSize,
                    BackgroundColor = style.BorderColor,
                });
            }
        }

        void Render(ListBlock block)
        {
            listScope++;

            for (var i = 0; i < block.Count(); i++)
            {
                var item = block.ElementAt(i);

                if (item is ListItemBlock itemBlock)
                {
                    Render(block, i + 1, itemBlock);
                }
            }

            listScope--;
        }

        void Render(ListBlock parent, int index, ListItemBlock block)
        {
            var initialStack = stack;

            stack = new StackLayout()
            {
                Spacing = Theme.Margin,
            };

            Render(block.AsEnumerable());

            var horizontalStack = new StackLayout
            {
                Orientation = StackOrientation.Horizontal,
                Margin = new Thickness(listScope * Theme.Margin, 0, 0, 0),
            };

            View bullet;

            if (parent.IsOrdered)
            {
                bullet = new Label
                {
                    Text = $"{index}.",
                    FontSize = Theme.Paragraph.FontSize,
                    TextColor = Theme.Paragraph.ForegroundColor,
                    VerticalOptions = LayoutOptions.Start,
                    HorizontalOptions = LayoutOptions.End,
                    LineHeight = Theme.Paragraph.LineHeight,
                };
            }
            else
            {
                bullet = new BoxView
                {
                    WidthRequest = 4,
                    HeightRequest = 4,
                    Margin = new Thickness(0, 6, 0, 0),
                    BackgroundColor = Theme.Paragraph.ForegroundColor,
                    VerticalOptions = LayoutOptions.Start,
                    HorizontalOptions = LayoutOptions.Center,
                };
            }

            horizontalStack.Children.Add(bullet);


            horizontalStack.Children.Add(stack);
            initialStack.Children.Add(horizontalStack);

            stack = initialStack;
        }

        void Render(HeadingBlock block)
        {
            MarkdownStyle style;

            switch (block.Level)
            {
                case 1:
                    style = Theme.Heading1;
                    break;
                case 2:
                    style = Theme.Heading2;
                    break;
                case 3:
                    style = Theme.Heading3;
                    break;
                case 4:
                    style = Theme.Heading4;
                    break;
                case 5:
                    style = Theme.Heading5;
                    break;
                default:
                    style = Theme.Heading6;
                    break;
            }

            var foregroundColor = isQuoted ? Theme.Quote.ForegroundColor : style.ForegroundColor;

            var label = new Label
            {
                FormattedText = CreateFormatted(block.Inline, style.FontFamily, style.Attributes, foregroundColor, style.BackgroundColor, style.FontSize, style.LineHeight),
            };

            AttachLinks(label);

            if (style.BorderSize > 0)
            {
                var headingStack = new StackLayout();
                headingStack.Children.Add(label);
                headingStack.Children.Add(new BoxView
                {
                    HeightRequest = style.BorderSize,
                    BackgroundColor = style.BorderColor,
                });
                stack.Children.Add(headingStack);
            }
            else
            {
                stack.Children.Add(label);
            }
        }

        void Render(ParagraphBlock block)
        {
            var style = Theme.Paragraph;
            var foregroundColor = isQuoted ? Theme.Quote.ForegroundColor : style.ForegroundColor;
            var label = new Label
            {
                FormattedText = CreateFormatted(block.Inline, style.FontFamily, style.Attributes, foregroundColor, style.BackgroundColor, style.FontSize, style.LineHeight),
            };
            AttachLinks(label);
            stack.Children.Add(label);
        }

        void Render(HtmlBlock block)
        {
            // ?
        }

        void Render(QuoteBlock block)
        {
            var initialIsQuoted = isQuoted;
            var initialStack = stack;

            isQuoted = true;
            stack = new StackLayout()
            {
                Spacing = Theme.Margin,
            };

            var style = Theme.Quote;

            if (style.BorderSize > 0)
            {
                var horizontalStack = new StackLayout()
                {
                    Orientation = StackOrientation.Horizontal,
                    BackgroundColor = Theme.Quote.BackgroundColor,
                };

                horizontalStack.Children.Add(new BoxView()
                {
                    WidthRequest = style.BorderSize,
                    BackgroundColor = style.BorderColor,
                });

                horizontalStack.Children.Add(stack);
                initialStack.Children.Add(horizontalStack);
            }
            else
            {
                stack.BackgroundColor = Theme.Quote.BackgroundColor;
                initialStack.Children.Add(stack);
            }

            Render(block.AsEnumerable());

            isQuoted = initialIsQuoted;
            stack = initialStack;
        }

        void Render(CodeBlock block)
        {
            var style = Theme.Code;
            var label = new Label
            {
                TextColor = style.ForegroundColor,
                FontAttributes = style.Attributes,
                FontFamily = style.FontFamily,
                FontSize = style.FontSize,
                Text = string.Join(Environment.NewLine, block.Lines),
                LineHeight = style.LineHeight,
            };
            stack.Children.Add(new Frame()
            {
                CornerRadius = 3,
                HasShadow = false,
                Padding = Theme.Margin,
                BackgroundColor = style.BackgroundColor,
                Content = label
            });
        }

        FormattedString CreateFormatted(ContainerInline inlines, string family, FontAttributes attributes, Color foregroundColor, Color backgroundColor, float size, float lineHeight)
        {
            var fs = new FormattedString();

            foreach (var inline in inlines)
            {
                var spans = CreateSpans(inline, family, attributes, foregroundColor, backgroundColor, size, lineHeight);
                if (spans != null)
                {
                    foreach (var span in spans)
                    {
                        fs.Spans.Add(span);
                    }
                }
            }

            return fs;
        }

        Span[] CreateSpans(Inline inline, string family, FontAttributes attributes, Color foregroundColor, Color backgroundColor, float size, float lineHeight)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    return new[]
                    {
                        new Span
                        {
                            Text = literal.Content.Text.Substring(literal.Content.Start, literal.Content.Length),
                            FontAttributes = attributes,
                            ForegroundColor = foregroundColor,
                            BackgroundColor = backgroundColor,
                            FontSize = size,
                            FontFamily = family,
                            LineHeight = lineHeight,
                        }
                    };

                case EmphasisInline emphasis:
                    var childAttributes = attributes | (emphasis.IsDouble ? FontAttributes.Bold : FontAttributes.Italic);
                    return emphasis.SelectMany(x => CreateSpans(x, family, childAttributes, foregroundColor, backgroundColor, size, lineHeight)).ToArray();

                case LineBreakInline breakline:
                    return new[] { new Span { Text = "\n" } };

                case LinkInline link:

                    var url = link.Url;

                    if (!(url.StartsWith("http://") || url.StartsWith("https://")))
                    {
                        url = $"{RelativeUrlHost?.TrimEnd('/')}/{url.TrimStart('/')}";
                    }

                    if (link.IsImage)
                    {
                        var image = new Image();

                        if (Path.GetExtension(url) == ".svg")
                        {
                            image.RenderSvg(url);
                        }
                        else
                        {
                            image.Source = url;
                        }

                        queuedViews.Add(image);
                        return new Span[0];
                    }
                    else
                    {
                        var spans = link.SelectMany(x => CreateSpans(x, Theme.Link.FontFamily ?? family, Theme.Link.Attributes, Theme.Link.ForegroundColor, Theme.Link.BackgroundColor, size, lineHeight)).ToArray();
                        links.Add(new KeyValuePair<string, string>(string.Join("", spans.Select(x => x.Text)), url));
                        return spans;
                    }

                case CodeInline code:
                    return new[]
                    {
                        new Span()
                        {
                            Text="\u2002",
                            FontSize = size,
                            FontFamily = Theme.Code.FontFamily,
                            ForegroundColor = Theme.Code.ForegroundColor,
                            BackgroundColor = Theme.Code.BackgroundColor
                        },
                        new Span
                        {
                            Text = code.Content,
                            FontAttributes = Theme.Code.Attributes,
                            FontSize = size,
                            FontFamily = Theme.Code.FontFamily,
                            ForegroundColor = Theme.Code.ForegroundColor,
                            BackgroundColor = Theme.Code.BackgroundColor
                        },
                        new Span()
                        {
                            Text="\u2002",
                            FontSize = size,
                            FontFamily = Theme.Code.FontFamily,
                            ForegroundColor = Theme.Code.ForegroundColor,
                            BackgroundColor = Theme.Code.BackgroundColor
                        },
                    };

                default:
                    Debug.WriteLine($"Can't render {inline.GetType()} inlines.");
                    return null;
            }
        }

        #endregion
    }
}