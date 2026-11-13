using System.Numerics;
using Dalamud.Bindings.ImGui;
using Steward.Core;

namespace Steward;

internal sealed record DecisionRow(Stock Stock,string Name,Decision Decision);

internal sealed class DecisionBrowser
{
    private IReadOnlyList<DecisionRow> rows=[];
    private int group;
    private readonly int[] totals=new int[6];
    private static readonly string[] Filters=["All","Disposal","Waiting","Protected / excluded","For sale","Collection"];
    private string query="";
    private Stock? selected;
    private DateTime? observed;
    internal void Clear() { rows=[];selected=null;observed=null;query="";group=0;Array.Clear(totals); }
    internal void Capture(IEnumerable<DecisionRow> items)
    {
        rows=items.OrderBy(row=>row.Name,StringComparer.OrdinalIgnoreCase).ThenBy(row=>row.Stock.Container).ThenBy(row=>row.Stock.Slot).ToArray();
        Array.Clear(totals);
        foreach(var row in rows){totals[0]++;totals[Group(row.Decision.Outcome)]++;}
        observed=DateTime.Now;
        selected=null;
    }
    private static int Group(Outcome outcome)=>outcome switch {
        Outcome.Dispose=>1, Outcome.Wait=>2, Outcome.Keep or Outcome.Ignore=>3,
        Outcome.Market=>4, _=>5
    };
    private static string Label(Outcome outcome)=>outcome switch {
        Outcome.Dispose=>"Disposal candidate",Outcome.Wait=>"Waiting for evidence",Outcome.Keep=>"Keep",
        Outcome.Ignore=>"Excluded",Outcome.Market=>"Eligible for sale",Outcome.CollectArmoire=>"Collect in armoire",
        Outcome.CollectDresser=>"Collect in dresser",Outcome.StoreRetainer=>"Protected overflow",_=>outcome.ToString()
    };
    internal void Draw(Action capture, Action<uint> edit, Action permissions)
    {
        if(ImGui.Button(observed.HasValue?"Refresh decisions":"Inspect carried items"))capture();
        if(observed is {} time){ImGui.SameLine();ImGui.TextDisabled(time.ToString("HH:mm:ss"));}
        ImGui.TextWrapped("Read-only snapshot of player inventory decisions. AutoRetainer checks eligibility again before disposal.");
        if(observed==null)
        {
            StewardShell.Panel("No snapshot",1,()=>ImGui.TextWrapped("Inspect carried items to see what Steward would keep, collect, sell or dispose of."));
            if(ImGui.Button("Disposal permissions"))permissions();
            return;
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##DecisionSearch","Filter by item name...",ref query,128);
        float right=ImGui.GetCursorScreenPos().X+ImGui.GetContentRegionAvail().X;
        for(int i=0;i<Filters.Length;i++)
        {
            string label=$"{Filters[i]}  {totals[i]}";
            if(i>0 && ImGui.GetItemRectMax().X+ImGui.GetStyle().ItemSpacing.X+ImGui.CalcTextSize(label).X+2*ImGui.GetStyle().FramePadding.X<=right)ImGui.SameLine();
            if(group==i)ImGui.PushStyleColor(ImGuiCol.Button,new Vector4(.12f,.39f,.35f,1));
            bool pressed=ImGui.Button(label);
            if(group==i)ImGui.PopStyleColor();
            if(pressed)group=i;
        }
        bool Matches(DecisionRow row)=>(group==0 || Group(row.Decision.Outcome)==group) && row.Name.Contains(query,StringComparison.OrdinalIgnoreCase);
        DecisionRow? detail=null;
        int count=0;
        foreach(var row in rows)
        {
            if(!Matches(row))continue;
            if(row.Stock==selected)detail=row;
            count++;
        }
        if(observed.HasValue)ImGui.TextDisabled($"{count} matching inventory slots");
        if(ImGui.BeginChild("Decisions",new(0,Math.Clamp(ImGui.GetContentRegionAvail().Y*.52f,72*StewardShell.Scale,265*StewardShell.Scale)),true))
        {
            bool columns=ImGui.GetContentRegionAvail().X>=450*StewardShell.Scale && ImGui.BeginTable("DecisionColumns",2,ImGuiTableFlags.SizingStretchProp|ImGuiTableFlags.RowBg);
            if(columns){ImGui.TableSetupColumn("Item");ImGui.TableSetupColumn("Decision");ImGui.TableHeadersRow();}
            foreach(var row in rows)
            {
                if(!Matches(row))continue;
                if(columns){ImGui.TableNextRow();ImGui.TableNextColumn();}
                var stock=row.Stock;
                if(ImGui.Selectable($"{row.Name}{(stock.Hq?" (HQ)":"")}  x{stock.Quantity}##{stock.Container}-{stock.Slot}",stock==selected))
                {selected=stock;detail=row;}
                if(columns){ImGui.TableNextColumn();ImGui.TextWrapped(Label(row.Decision.Outcome));}
            }
            if(columns)ImGui.EndTable();
            if(count==0)ImGui.TextWrapped(observed.HasValue?"No items match this filter.":"Inspect carried items to load their decisions.");
        }
        ImGui.EndChild();
        if(detail is {} d)
        {
            StewardShell.Section(d.Name);
            ImGui.TextWrapped(Label(d.Decision.Outcome)+"  /  "+d.Decision.Reason);
            if(d.Decision.Rule is {} rule)ImGui.TextWrapped("Sale rule: "+rule.Name);
            if(d.Decision.MaximumRemovable is {} removable)ImGui.TextWrapped($"Maximum removable: {removable}");
            if(ImGui.Button("Edit this item's policy"))edit(d.Stock.ItemId);
        }
        else if(count>0)ImGui.TextWrapped("Select an item to see its decision and reason.");
        if(ImGui.Button("Disposal permissions"))permissions();
    }
}

internal sealed class RunDesk
{
    private int selected;
    private static readonly string[] Passes=["Retainer market","Glamour dresser","Armoire","Full cleanup"];
    private static readonly string[] Details=["List and reprice on the current retainer using the enabled market features.",
        "Collect eligible gear into the glamour dresser. Enabled dresser cleanup can also restore items.",
        "Store eligible pieces in the armoire.","Visit included retainers and use the enabled AutoRetainer disposal methods, in order: GC delivery, desynthesis, then vendor sale."];
    internal void Draw(string context,int saleRules,int? freeSlots,int? overflow,string? hold,Action recovery,Func<int,bool> enabled,Action<int> start,Action inspect,Action readiness)
    {
        ImGui.TextWrapped(hold??context);
        if(hold!=null)recovery();
        ImGui.Spacing();
        AetherglassUI.Controls.Row("pass","Manual pass",Details[selected],()=>{
            var next=AetherglassUI.Controls.Choice("##pass",Passes[selected],Passes);
            selected=Array.IndexOf(Passes,next);
        });
        bool available=enabled(selected);
        if(!available)ImGui.TextWrapped("This pass is disabled by your feature settings or an active cleanup.");
        ImGui.BeginDisabled(hold!=null||!available);
        if(ImGui.Button("Run "+Passes[selected].ToLowerInvariant()))start(selected);
        ImGui.EndDisabled();
        ImGui.Spacing();
        AetherglassUI.ReviewControls.Metric("rules","Enabled sale rules",saleRules,"");
        AetherglassUI.ReviewControls.Metric("free","Free dresser slots",freeSlots,"Latest observation");
        AetherglassUI.ReviewControls.Metric("pending","Awaiting collection",overflow,"Protected items",readiness);
        if(ImGui.Button("Inspect inventory decisions"))inspect();
    }
}
