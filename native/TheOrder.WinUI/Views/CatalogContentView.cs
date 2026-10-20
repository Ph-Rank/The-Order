using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TheOrder.Domain;
using TheOrder.Application;
using TheOrder.Presentation;
namespace TheOrder.WinUI.Views;
public sealed class CatalogContentView : StackPanel,IImageHost
{
    public IImagePipeline Images {get;set;}=null!;
    public IRemoteCoverRegistry? Registry {get;set;}
    public Func<CatalogEntityRef,Task>? Browse {get;set;}
    public Func<string,Task>? OpenLink {get;set;}
    public UIElement? Sources { get; set; }
    public UIElement? BeforeTags { get; set; }
    public bool ShowRelatedWorks { get; set; } = true;
    private readonly CatalogReleasesView releases = new();
    private ICatalogReleaseReader? releasesReader;
    private string? releasesWorkId;
    public Func<long>? ReleasesAuthGeneration { get; set; }
    public ICatalogReleaseReader? ReleasesReader
    {
        get => releasesReader;
        set { if (ReferenceEquals(releasesReader, value)) return; releasesReader = value;
            if (value is null) releases.Deactivate(); else { releases.Configure(value, ReleasesAuthGeneration); releases.SetSource(releasesWorkId); } }
    }
    public void ConfigureReleases(ICatalogReleaseReader? reader, Func<long>? authGeneration = null)
    {
        ReleasesAuthGeneration = authGeneration; releasesReader = reader;
        if (reader is null) releases.Deactivate(); else { releases.Configure(reader, authGeneration); releases.SetSource(releasesWorkId); }
    }
    public void DeactivateReleases() { releasesWorkId = null; releases.Deactivate(); }
    internal object LanguageEvidence => Children.OfType<ContentLanguageInfo>().Select(x => x.Evidence).ToArray();
    internal object ReleaseEvidence => releases.Evidence;
    internal async Task<object> ReleasesFixtureAsync(string action, string? value = null)
    {
        switch (action)
        {
            case "expand":
                var idBefore = releasesWorkId; await releases.ExpandAsync();
                if (releasesWorkId == idBefore && idBefore is not null && IsLoaded) releases.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false }); break;
            case "collapse": releases.Collapse(); break;
            case "next": await releases.NextAsync(); break;
            case "refresh": await releases.RefreshAsync(); break;
            case "cancel": releases.Cancel(); break;
            case "inspect": break;
            case "filter":
                var filters = (value ?? "").Split('|');
                releases.FilterForFixture(filters.ElementAtOrDefault(0), filters.ElementAtOrDefault(1), filters.ElementAtOrDefault(2)); break;
            case "row" when value is not null: releases.ExpandRowForFixture(value); break;
            default: throw new ArgumentException("Unknown releases fixture action.", nameof(action));
        }
        return ReleaseEvidence;
    }
    private readonly List<(string Kind,CoverPresenter View)> artwork=[];
    internal object ImageEvidence=>artwork.Select(a=>new{a.Kind,a.View.SourceStatus,a.View.HasImage,width=a.View.ActualWidth,height=a.View.ActualHeight}).ToArray();
    // Prime the exact display buckets before binding cards. Originals were fetched by import;
    // reopening at another DPI decodes only local bytes, without painting a loading placeholder.
    public async Task PrepareImagesAsync(WorkMetadata work,double scale,CancellationToken token)
    {
        var rows=(work.Presentation?.Characters.Select(c=>(c.Entity.ImageUrl,122d,146d))??work.Characters.Select(c=>(c.ImageUrl,122d,146d)))
            .Concat((ShowRelatedWorks ? CatalogRelatedWorks.Visible(work.Presentation) : []).Select(w=>(w.ImageUrl,146d,196d)))
            .Where(r=>r.ImageUrl is {Length:>0}).DistinctBy(r=>r.ImageUrl).Take(164);
        foreach(var (url,width,height) in rows)
        {
            token.ThrowIfCancellationRequested();var key=Registry?.Register(url,work.FetchedAt);if(key is null)continue;
            await Images.LoadAsync(key,ThumbnailSizing.Edge(width,height,scale),token);
        }
    }
    public CatalogContentView(){Spacing=22;Unloaded+=(_,_)=>{if(!IsLoaded)releases.Deactivate();};}
    private static TextBlock Text(string text,double size=15)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true};
    private static TextBlock Caption(string text,double size=14)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Stretch};
    private static StackPanel Section(string title)=>new(){Spacing=10,Children={new TextBlock{Text=title,FontSize=22,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold}}};
    private HyperlinkButton Link(CatalogEntityRef entity){var b=new HyperlinkButton{Content=entity.Name,FontSize=15,Padding=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,entity.Name+UiText.Get("，资料详情"));b.IsEnabled=TheOrder.Infrastructure.Online.RemoteWorkDto.ValidId(entity.Id);b.Click+=async(_,_)=>{if(Browse is not null)await Browse(entity);};return b;}
    private Border TagChip(string name, CatalogEntityRef? entity) => CatalogTagChip.Create(name, entity, Browse);
    private MarkdownView Markdown(string text)=>new(){Text=text,Images=Images,Registry=Registry,OpenLink=OpenLink};
    public void SetWork(WorkMetadata? work,string? personalName=null,bool collapseFacts=false)
    {
        releasesWorkId = work?.Id; releases.OpenLink = OpenLink; releases.SetSource(releasesWorkId);
        var rawWork = work;
        if (work is not null) work = CatalogLanguage.Project(work);
        // Keep the active PV host attached while metadata/language sections are
        // refreshed. Detaching it would stop playback on an unrelated late response.
        foreach (var child in Children.Where(x => x != BeforeTags).ToArray()) Children.Remove(child);
        bool passedVideoSlot = false;
        void AddSection(UIElement child)
        {
            if (ReferenceEquals(child, BeforeTags))
            {
                if (!Children.Contains(child)) Children.Add(child);
                passedVideoSlot = true;
            }
            else if (!passedVideoSlot && BeforeTags is not null && Children.Contains(BeforeTags)) Children.Insert(Children.IndexOf(BeforeTags), child);
            else Children.Add(child);
        }
        artwork.Clear();if(work is null){AddSection(Text(UiText.Get("尚未关联作品。可在更多操作中重新识别。")));return;}
        var p=work.Presentation;
        if (!collapseFacts)
        {
        var facts=Section(UiText.Get("作品信息"));var names=new[]{work.Title,work.OriginalTitle}.Where(n=>!string.IsNullOrWhiteSpace(n)&&n!=personalName).Distinct().ToArray();if(names.Length>0)facts.Children.Add(Text(string.Join(" / ",names)));
        if(p is {Companies.IsEmpty:false}) {foreach(var group in p.Companies.GroupBy(c=>CatalogReleaseFacts.CompanyCaption(c.Relation))) {var row=new TagFlowPanel{HorizontalSpacing=6,VerticalSpacing=4};row.Children.Add(Text(UiText.Get(group.Key+" ·")));foreach(var company in group)row.Children.Add(Link(company));facts.Children.Add(row);}}else if(!work.Developers.IsEmpty)facts.Children.Add(Text(UiText.Get("制作 · ")+string.Join(" / ",work.Developers)));
        if(!string.IsNullOrWhiteSpace(work.ReleaseDate))facts.Children.Add(Text(UiText.Get("最早收录发行 · ")+CatalogReleaseFacts.WorkDate(work.ReleaseDate,work.ReleaseDatePrecision)));
        if(!work.Ratings.IsEmpty)facts.Children.Add(Text(UiText.Get("本版本评分 · ")+string.Join(" / ",work.Ratings.Select(r=>$"{SiteName(r.Source)} {r.Score:0.#}/{r.Scale:0.#}"+(r.Votes is { } votes?UiText.Format($"（{votes:0} 人）"):"")))));
        if(p is not null){foreach(var (name,items) in new[]{(UiText.Get("引擎"),p.Engines),(UiText.Get("系列"),p.Series)})if(!items.IsEmpty){var row=new TagFlowPanel{HorizontalSpacing=6,VerticalSpacing=4};row.Children.Add(Text(name+" ·"));foreach(var entity in items)row.Children.Add(Link(entity));facts.Children.Add(row);}}
        // The hero already contains these facts. Preserve the series link, which is not in the hero.
        AddSection(facts);
        }
        else if (p is { Series.IsEmpty: false })
        {
            var series = new TagFlowPanel { HorizontalSpacing = 6, VerticalSpacing = 4, Margin = new Thickness(0, 0, 0, -6) };
            var seriesLabel = Text(UiText.Get("系列 ·"), 14);
            seriesLabel.Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextSecondaryBrush"];
            series.Children.Add(seriesLabel);
            foreach (var entity in p.Series) series.Children.Add(Link(entity));
            AddSection(series);
        }
        if (collapseFacts && p is { Companies.IsEmpty: false })
            foreach (var group in p.Companies.Where(company => company.Relation != "developer").GroupBy(company => CatalogReleaseFacts.CompanyCaption(company.Relation)))
            {
                var organizations = new TagFlowPanel { HorizontalSpacing = 6, VerticalSpacing = 4 };
                organizations.Children.Add(Text(UiText.Get(group.Key + " ·"), 14));
                foreach (var company in group) organizations.Children.Add(Link(company));
                AddSection(organizations);
            }
        if (releasesReader is not null) AddSection(releases);
        var intro = Section(UiText.Get("简介"));
        if (rawWork is not null && ContentLanguage.CatalogSynopsis(rawWork) is { Selected.IsMachine: true } selectedIntro)
        {
            var caption = Text(selectedIntro.Caption, 12); caption.Opacity = .7; intro.Children.Add(caption);
        }
        var description = Markdown(string.IsNullOrWhiteSpace(work.Description) ? UiText.Get("暂无本版本简介。") : work.Description);
        description.MaxWidth = 860; description.HorizontalAlignment = HorizontalAlignment.Left;
        intro.Children.Add(description); AddSection(intro);
        if (rawWork is not null)
        {
            var languageInfo = new ContentLanguageInfo();
            languageInfo.Show(ContentLanguage.Catalog(new("NextMoe", "Work", rawWork.Id), "title", rawWork.Presentation?.Names, rawWork.OriginalTitle.Length > 0 ? rawWork.OriginalTitle : rawWork.Title),
                ContentLanguage.CatalogSynopsis(rawWork), ContentLanguage.Aliases(new("NextMoe", "Work", rawWork.Id), rawWork.Aliases));
            AddSection(languageInfo);
        }
        if (BeforeTags is not null) AddSection(BeforeTags);
        if (!work.Tags.IsEmpty)
        {
            var tags = Section(UiText.Get("标签"));
            var flow = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            void AddTags(int count)
            {
                flow.Children.Clear();
                foreach (var name in work.Tags.Take(count))
                    flow.Children.Add(TagChip(name, p?.Tags.FirstOrDefault(tag => tag.Name == name)));
            }
            AddTags(16);
            tags.Children.Add(flow);
            if (work.Tags.Length > 16)
            {
                var more = new HyperlinkButton { Content = UiText.Format($"全部 {work.Tags.Length} 个标签"), FontSize = 13, Padding = new Thickness(0), Margin = new Thickness(0, 2, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                bool all = false;
                more.Click += (_, _) => { all = !all; AddTags(all ? 200 : 16); more.Content = all ? UiText.Get("收起标签") : UiText.Format($"全部 {work.Tags.Length} 个标签"); };
                tags.Children.Add(more);
            }
            AddSection(tags);
        }
        var characters=p?.Characters??[..work.Characters.Select(c=>new CatalogCharacter(new(CatalogEntityKind.Character,c.Id,c.Name,ImageUrl:c.ImageUrl),c.OriginalName,c.Role,"none",[..c.VoiceActors.Select(n=>new CatalogEntityRef(CatalogEntityKind.CreditName,"",n))]))];
        bool hasSpoilerExpander = false;
        if(!characters.IsEmpty){var section=Section(UiText.Get("角色"));var safe=characters.Where(c=>c.Spoiler is "" or "none").ToArray();section.Children.Add(CharacterGrid(safe,work.FetchedAt));var hidden=characters.Where(c=>c.Spoiler is not ("" or "none")).ToArray();if(hidden.Length>0){hasSpoilerExpander = true;var expander=new Expander{Header=UiText.Format($"剧透角色（{hidden.Length}）"),HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,4,0,0)};expander.Expanding+=(_,_)=>{expander.Content??=CharacterGrid(hidden,work.FetchedAt);};section.Children.Add(expander);}AddSection(section);}
        bool hasCreditsExpander = false;
        if (p is { Credits.IsEmpty: false })
        {
            var credits = new CreditColumnsPanel();
            foreach (var group in p.Credits.Where(g => !CatalogStaffRoles.IsVoiceActing(g.Role, g.RoleKey)))
            {
                var row = new StackPanel { Spacing = 8 };
                var role = Text(CatalogStaffRoles.Name(group.Role, group.RoleKey), 13);
                role.Opacity = .7; role.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                row.Children.Add(role);
                var flow = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
                foreach (var person in group.People.Take(80)) flow.Children.Add(Link(person));
                row.Children.Add(flow);
                if (group.People.Length > 80)
                {
                    var remaining = new Expander { Header = UiText.Format($"另外 {group.People.Length - 80} 人"), HorizontalAlignment = HorizontalAlignment.Stretch };
                    remaining.Expanding += (_, _) => { if (remaining.Content is not null) return; var more = new TagFlowPanel { HorizontalSpacing = 8, VerticalSpacing = 4 }; foreach (var person in group.People.Skip(80)) more.Children.Add(Link(person)); remaining.Content = more; };
                    row.Children.Add(remaining);
                }
                credits.Children.Add(new Border { Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["CatalogCreditCardStyle"], Child = row });
            }
            if (credits.Children.Count > 0)
            {
                hasCreditsExpander = true;
                AddSection(new Expander
                {
                    Header = UiText.Get("制作人员"),
                    Content = credits,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = hasSpoilerExpander ? new Thickness(0, -12, 0, 0) : new Thickness(0)
                });
            }
        }
        if (Sources is not null)
        {
            if (Sources is FrameworkElement sourcesElement)
                sourcesElement.Margin = (hasCreditsExpander || hasSpoilerExpander) ? new Thickness(0, -12, 0, 0) : new Thickness(0);
            AddSection(Sources);
        }
        if(ShowRelatedWorks && p is {RelatedWorks.IsEmpty:false}){var related=Section(UiText.Get("相关作品"));related.Children.Add(RelatedGrid(p.RelatedWorks,work.FetchedAt));AddSection(related);}
    }
    private TagFlowPanel RelatedGrid(IEnumerable<CatalogEntityRef> works,string revision)
    {
        var flow=new TagFlowPanel {HorizontalSpacing=16,VerticalSpacing=18};
        foreach(var entity in works.Where(CatalogRelatedWorks.Eligible).Take(64))
        {
            var card=new StackPanel {Width=146,Spacing=6};
            var picture=new CoverPresenter {Pipeline=Images,SourceKey=Registry?.Register(entity.ImageUrl,revision)??ImageSourceKeys.Remote(entity.ImageUrl,revision),ImageCornerRadius=6};artwork.Add(("related",picture));
            card.Children.Add(new Border {Width=146,Height=196,CornerRadius=new CornerRadius(6),Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["SubtleFillColorSecondaryBrush"],Child=picture});
            var title=Caption(entity.Name);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;card.Children.Add(title);
            if(entity.Subtitle is {Length:>0}){var sub=Caption(entity.Subtitle,12);sub.Opacity=0.78;card.Children.Add(sub);}
            var button=new Button {Content=card,Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),HorizontalContentAlignment=HorizontalAlignment.Stretch};
            button.IsEnabled=TheOrder.Infrastructure.Online.RemoteWorkDto.ValidId(entity.Id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,entity.Name+(entity.Subtitle is {Length:>0} relation?"，"+relation:""));
            button.Click+=async(_,_)=>{if(Browse is not null)await Browse(entity);};
            flow.Children.Add(button);
        }
        return flow;
    }
    private static string RoleLabel(string? role) => (role ?? "").Trim().ToLowerInvariant() switch
    { "main" => UiText.Get("主角"), "secondary" => UiText.Get("配角"), "appears" or "cameo" => UiText.Get("客串"), _ => "" };
    private StackPanel CharacterGrid(IEnumerable<CatalogCharacter> characters,string revision)
    {
        var result=new StackPanel {Spacing=14};
        var illustrated=new TagFlowPanel();
        var compact=new TagFlowPanel {HorizontalSpacing=12,VerticalSpacing=10};
        foreach(var c in characters.Take(100))
        {
            var roleText = RoleLabel(c.Role);
            var tipText = string.Join(" · ", new[] { string.IsNullOrWhiteSpace(c.OriginalName) ? c.Entity.Name : c.OriginalName, roleText }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if(c.Entity.ImageUrl is not {Length:>0})
            {
                // Missing source artwork has no image frame or placeholder. Render as a neat
                // compact badge card so text-only characters feel deliberate and structured.
                var name=Caption(c.Entity.Name);name.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;name.TextWrapping=TextWrapping.NoWrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.MaxLines=1;
                var button=new Button {Content=name,Padding=new Thickness(0),MinHeight=0,BorderThickness=new Thickness(0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
                button.IsEnabled=TheOrder.Infrastructure.Online.RemoteWorkDto.ValidId(c.Entity.Id);
                button.Click+=async(_,_)=>{if(Browse is not null)await Browse(c.Entity);};ToolTipService.SetToolTip(button,tipText);
                var voices=CompactVoices(c);voices.Opacity=0.76;
                var badge=new Border
                {
                    Width=172,
                    Padding=new Thickness(12,8,12,8),
                    CornerRadius=new CornerRadius(6),
                    Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                    BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["StrokeBrush"],
                    BorderThickness=new Thickness(1),
                    Child=new StackPanel {Spacing=3,Children={button,voices}}
                };
                compact.Children.Add(badge);
                continue;
            }
            var card=new StackPanel {Width=122,Spacing=4,Margin=new Thickness(0,0,12,16)};
            var art=new StackPanel {Spacing=6};
            var portrait=new CoverPresenter {Pipeline=Images,SourceKey=Registry?.Register(c.Entity.ImageUrl,revision)??ImageSourceKeys.Remote(c.Entity.ImageUrl,revision),PreserveEntireImage=true,ImageCornerRadius=6};
            artwork.Add(("character",portrait));
            art.Children.Add(new Border
            {
                Width=122,
                Height=146,
                CornerRadius=new CornerRadius(6),
                Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                Child=portrait
            });
            var charName=Caption(c.Entity.Name);charName.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
            art.Children.Add(charName);
            var illustratedButton=new Button {Content=art,Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),HorizontalContentAlignment=HorizontalAlignment.Stretch};
            illustratedButton.IsEnabled=TheOrder.Infrastructure.Online.RemoteWorkDto.ValidId(c.Entity.Id);illustratedButton.Click+=async(_,_)=>{if(Browse is not null)await Browse(c.Entity);};ToolTipService.SetToolTip(illustratedButton,tipText);card.Children.Add(illustratedButton);
            if(c.Voices.IsEmpty){var noCv=Caption(UiText.Get("未收录声优"),12);noCv.Opacity=0.65;card.Children.Add(noCv);}else foreach(var voice in c.Voices.Take(3)){if(voice.Id.Length>0){var link=Link(voice);link.Content=Caption("CV "+voice.Name,12);link.FontSize=12;link.Padding=new Thickness(0);link.HorizontalAlignment=HorizontalAlignment.Stretch;link.HorizontalContentAlignment=HorizontalAlignment.Center;card.Children.Add(link);}else{var cv=Caption("CV "+voice.Name,12);cv.Opacity=0.82;card.Children.Add(cv);}}
            illustrated.Children.Add(card);
        }
        if(illustrated.Children.Count>0)result.Children.Add(illustrated);
        if(compact.Children.Count>0)result.Children.Add(compact);
        return result;
    }
    private TextBlock CompactVoices(CatalogCharacter character)
    {
        var line=Caption(character.Voices.IsEmpty?UiText.Get("未收录声优"):"",12);
        line.TextWrapping=TextWrapping.NoWrap;line.TextTrimming=TextTrimming.CharacterEllipsis;line.MaxLines=1;
        if(character.Voices.IsEmpty)return line;
        line.Inlines.Clear();
        line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run {Text="CV "});
        var first=true;
        foreach(var voice in character.Voices.Take(16))
        {
            if(!first)line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run {Text=" / "});
            first=false;
            if(TheOrder.Infrastructure.Online.RemoteWorkDto.ValidId(voice.Id))
            {
                var link=new Microsoft.UI.Xaml.Documents.Hyperlink {UnderlineStyle=Microsoft.UI.Xaml.Documents.UnderlineStyle.None};link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run {Text=voice.Name});
                link.Click+=async(_,_)=>{if(Browse is not null)await Browse(voice);};line.Inlines.Add(link);
            }
            else line.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run {Text=voice.Name});
        }
        ToolTipService.SetToolTip(line,"CV "+string.Join(" / ",character.Voices.Select(v=>v.Name)));
        return line;
    }
    public static string SiteName(string site)=>site switch{"curated" or "kungal" or "nextmoe"=>"KunGal","howlongtobeat"=>"HowLongToBeat","bangumi"=>"Bangumi","vndb"=>"VNDB","erogamescape"=>"ErogameScape","dlsite"=>"DLsite",_=>site};
}
