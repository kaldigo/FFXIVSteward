using System.Numerics;
using Dalamud.Bindings.ImGui;
using Glasswork;
using Glasswork.Backend.Preview;
using Steward.Core;

namespace Steward;

// Offline adapters for game-owned state and actions.
public sealed partial class Plugin
{
    private readonly StewardShell shell = new();
    private readonly Configuration config=new(){Rules=new(){Sales=[new(){Name="General items",MinimumUnit=50000,MinimumTotal=50000,MaximumStack=1},new(){Name="Materials",MaximumStack=30,AllowLowPriority=true}]}};
    private sealed class SampleView {
        public Dictionary<uint,string> SaleCategories {get;}=[];
        public Dictionary<ulong,string> RetainerNames {get;}=new(){[1]="Example retainer"};
        public IReadOnlyCollection<ulong> Retainers=>RetainerNames.Keys;
        public uint ItemStackLimit(uint id)=>999;
        public ulong Character=>1;
        public Evidence Evidence {get;}=new(){DresserFreeSlots=37};
        public bool NativeReferencesAvailable=>true;
        public bool NativeReferencesCached=>true;
        public DateTime NativeReferencesObservedAt=>new(2026,11,1);
        public ItemFacts Facts(uint id)=>new(id,"Sample item",false,1,1,true,false,SourceKind.Ordinary,1);
        public IReadOnlyList<(uint Id,string Name)> SearchItems(string query)=>[];
    }
    private readonly SampleView view=new();
    private readonly ulong attached=1;
    private readonly SamplePlayer player=new();
    private record SamplePlayer(ulong ContentId=1);
    private uint selectedItem;
    private readonly Dictionary<uint,string> itemQueries=[];
    private readonly Dictionary<uint,IReadOnlyList<(uint Id,string Name)>> itemSearchResults=[];
    private readonly List<string> cleanupPreview=[],history=[];
    private readonly string status="Waiting for inventory review",ReviewPath="Sample review log";
    private record ServiceStatus(string Status="Ready",string Freshness="Cached prices available");
    private readonly ServiceStatus imports=new(),universalis=new(),tracked=new(),dungeonSources=new();
    private long nextReview { get; set; }
    private sealed class Journal {public void Reset(){}}
    private readonly Journal reviewJournal=new();
    private sealed class Market {public void CleanupDresser(){} public string[] PreviewDresserCleanup()=>[];}
    private readonly Market market=new();
    private void Record(string text){}
    private void Save(){}
    private void SavePolicy(){}
    private void RefreshProtection(){}
    private void StartOverflow(){}
    private void DetachPolicy(){}
    private void DrawSaleRuleCategories(SaleRule rule)=>DrawCategorySelection("Included categories",rule.Categories,"All categories","");
    private void DrawCategorySelection(string label,HashSet<uint> selected,string emptyLabel,string description,bool nonEquipmentOnly=false,bool saleCategories=false)
    {if(StewardShell.CategoryHeader(label,emptyLabel))ImGui.TextWrapped(description);}
    private void DrawSalePreview(){
        Section("Current retainer preview");
        StewardShell.Panel("No market preview",1,()=>ImGui.TextWrapped("Open a retainer's selling list to load proposed listings, price changes and withdrawals."));
    }
    private void DrawDecisions()=>decisions.Draw(()=>{},_=>Open(Desk.Items),()=>Open(Desk.Disposal));
    private void DrawOverviewPage()=>runDesk.Draw("Log-only review",2,37,6,"Log-only mode is recording proposals. Manual passes are paused.",()=>{},_=>true,_=>{},()=>Open(Desk.Inventory),()=>Open(Desk.Readiness));
    private bool executionFault;
    private readonly RunDesk runDesk=new();
    private readonly DecisionBrowser decisions=new();
    internal Plugin()
    {
        decisions.Capture([
            new(new Stock(1,false,5,1,0),"Gold Chocobo Feather",new(Outcome.Keep,"Special acquisition source")),
            new(new Stock(2,false,1,1,1),"Shisui Zori of Scouting",new(Outcome.Dispose,"Surplus collection copy")),
            new(new Stock(3,false,24,1,2),"Zonure Leather",new(Outcome.Market,"Matches sale thresholds",config.Rules.Sales[1])),
            new(new Stock(4,false,1,1,3),"Metallic Silver Dye",new(Outcome.Ignore,"Explicit item exclusion"))]);
    }
    private static void Section(string title) => StewardShell.Section(title);
    private void StartCleanup() { }
    private void Stop() { }
    private void Setting(string title,bool value,Action<bool> update,string? description=null)
    {
        if(StewardShell.SwitchRow(title,value,description)){update(!value);SavePolicy();}
    }
    internal void Open(Desk page) => shell.Open(page);
    internal void Draw(bool held)
    {
        executionFault=held;
        string context=held?"Inventory review needed":"Log-only review";
        var p=ImGui.GetWindowPos();
        ImGui.GetWindowDrawList().AddRectFilled(p,p+ImGui.GetWindowSize(),0xff130f0e);
        shell.Draw(context,()=>{},()=>{},DrawWorkspace);
    }
}
public abstract class Screen : IPreviewScreen
{
    private readonly Plugin plugin=new();
    internal Screen(Desk page)=>plugin.Open(page);
    public abstract string Name {get;}
    public abstract Vector2 WindowSize {get;}
    public Look Look => Look.NeutralDark();
    public virtual ReadOnlySpan<PreviewInput> Script => [];
    public void Draw()=>plugin.Draw(Name.Contains("review"));
}
public sealed class Activity():Screen(Desk.Activity){
    public override string Name=>"steward-activity"; public override Vector2 WindowSize=>new(780,610);
}
public sealed class Compact():Screen(Desk.Activity){
    public override string Name=>"steward-compact"; public override Vector2 WindowSize=>new(420,430);
}
public sealed class Filters():Screen(Desk.Glamour){
    public override string Name=>"steward-filters"; public override Vector2 WindowSize=>new(680,650);
}
public sealed class Review():Screen(Desk.Review){
    public override string Name=>"steward-review"; public override Vector2 WindowSize=>new(420,430);
}
public sealed class Navigation():Screen(Desk.Activity){
    public override string Name=>"steward-navigation"; public override Vector2 WindowSize=>new(620,510);
    private static readonly PreviewInput[] Events=[
        new(3,PreviewInputKind.Press,new(36,119)),new(4,PreviewInputKind.Release,new(36,119)),
        new(8,PreviewInputKind.Move,new(36,119)),new(65,PreviewInputKind.Move,new(36,119))];
    public override ReadOnlySpan<PreviewInput> Script=>Events;
}
public sealed class Sales():Screen(Desk.Sales){
    public override string Name=>"steward-sales"; public override Vector2 WindowSize=>new(620,510);
}
public sealed class Back():Screen(Desk.Activity){
    public override string Name=>"steward-back"; public override Vector2 WindowSize=>new(620,510);
    private static readonly PreviewInput[] Events=[
        new(3,PreviewInputKind.Press,new(129,108)),new(4,PreviewInputKind.Release,new(129,108)),
        new(8,PreviewInputKind.Press,new(109,65)),new(9,PreviewInputKind.Release,new(109,65)),
        new(12,PreviewInputKind.Move,new(11,10))];
    public override ReadOnlySpan<PreviewInput> Script=>Events;
}
public sealed class Inventory():Screen(Desk.Inventory){
    public override string Name=>"steward-inventory"; public override Vector2 WindowSize=>new(720,590);
    private static readonly PreviewInput[] Select=[new(4,PreviewInputKind.Press,new(160,250)),new(5,PreviewInputKind.Release,new(160,250))];
    public override ReadOnlySpan<PreviewInput> Script=>Select;
}
public sealed class PageCheck : Screen
{
    public PageCheck():base(Enum.Parse<Desk>(Environment.GetEnvironmentVariable("STEWARD_PREVIEW_PAGE")??"Activity")){}
    public override string Name=>"steward-page";
    public override Vector2 WindowSize=>Environment.GetEnvironmentVariable("STEWARD_PREVIEW_WIDTH")=="compact"?new(420,430):new(860,650);
}
