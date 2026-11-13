using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Steward;

internal enum Desk { Activity, Sales, Glamour, Storage, Items, Connections, Review, Strategies, Inventory, Setup, Market, Collection, Disposal, Readiness }

internal sealed class StewardShell
{
    private static readonly Desk[] Rail = [Desk.Activity, Desk.Inventory, Desk.Market, Desk.Collection, Desk.Setup];
    private static readonly string[] Names = ["Run desk", "Sale policy", "Glamour protection", "Retainer & dresser storage", "Item overrides", "Integrations", "Developer tools", "Dedicated retainer strategy", "Inventory decisions", "Setup", "Retainer market", "Collection", "Disposal permissions", "Inventory readiness"];
    private static readonly string[] Topics = ["run cleanup pass", "market undercut stacks quality materia pricing retainers", "plates outfits acquisition rarity gear", "dresser armoire overflow entrust categories", "keep exclude reserve protection", "AutoRetainer Allagan connection", "log only diagnostics snapshot history", "metallic silver dye quotas buyout", "inspect item decision reason held disposal", "configure integrations diagnostics", "listing prices proposals withdrawals", "collection storage space", "category permission discard vendor desynthesis", "missing evidence plate cache"];
    private string search = "";
    private bool results;
    internal Desk Current { get; private set; }
    private readonly Stack<(Desk Page,bool Results)> history=new();
    internal void Open(Desk page)
    {
        if(page!=Current || results) history.Push((Current,results));
        Current=page;
        results=false;
    }
    private static Desk Area(Desk page)=>page switch {
        Desk.Sales or Desk.Strategies=>Desk.Market,
        Desk.Glamour or Desk.Storage=>Desk.Collection,
        Desk.Items or Desk.Disposal=>Desk.Inventory,
        Desk.Review or Desk.Connections=>Desk.Setup,
        Desk.Readiness=>Desk.Activity,
        _=>page
    };

    internal static void Link(string title,string detail, Action open)
    {
        ImGui.Spacing();
        if(ImGui.Button(title,new Vector2(-1,0))) open();
        ImGui.TextWrapped(detail);
    }
    internal static float Scale => ImGui.GetTextLineHeight() / 16f;
    internal static bool CategoryHeader(string label,string summary)
    {
        if(ImGui.CalcTextSize(label+" - "+summary).X+ImGui.GetFrameHeight()*2<=ImGui.GetContentRegionAvail().X)
            return ImGui.CollapsingHeader($"{label} - {summary}###{label}");
        ImGui.TextWrapped(label);
        return ImGui.CollapsingHeader($"{summary}###{label}");
    }
    internal static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped(title);
    }
    internal static void Panel(string id,int columns,Action draw)
    {
        float scale=Scale;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding,new Vector2(10,7)*scale);
        if(ImGui.BeginTable(id,columns,ImGuiTableFlags.SizingStretchSame|ImGuiTableFlags.NoSavedSettings|ImGuiTableFlags.PadOuterX))
        {
            ImGui.TableNextRow();
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0,0xff292423);
            ImGui.TableNextColumn();
            draw();
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        ImGui.Spacing();
    }

    internal static bool SwitchRow(string title,bool value,string? description)
    {
        bool changed=false;
        ImGui.PushID(title);
        Panel("setting",1,()=>{
            float height=ImGui.GetFrameHeight();
            float width=ImGui.GetContentRegionAvail().X;
            float control=height*1.9f;
            var origin=ImGui.GetCursorScreenPos();
            ImGui.BeginGroup();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX()+Math.Max(40,width-control-14*Scale));
            ImGui.TextUnformatted(title);
            if(description!=null && ImGui.IsItemHovered())ImGui.SetTooltip(description);
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(origin.X+width-control,origin.Y));
            var pos=ImGui.GetCursorScreenPos();
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,height/2);
            ImGui.PushStyleColor(ImGuiCol.Button,value?new Vector4(.10f,.48f,.43f,1):new Vector4(.23f,.24f,.28f,1));
            changed=ImGui.Button("###"+title,new Vector2(control,height));
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            var on=changed?!value:value;
            var knob=pos+new Vector2(on?control-height/2:height/2,height/2);
            ImGui.GetWindowDrawList().AddCircleFilled(knob,height*.34f,0xfff3f1ec);
            if((ImGui.IsItemHovered()||ImGui.IsItemFocused())&&description!=null) ImGui.SetTooltip(description);
        });
        ImGui.PopID();
        return changed;
    }

    internal static void Metric(string label,string value)
    {
        ImGui.TextWrapped(value);
        ImGui.PushStyleColor(ImGuiCol.Text,new Vector4(.72f,.73f,.76f,1));
        ImGui.TextWrapped(label);
        ImGui.PopStyleColor();
    }
    internal static void Diagnostics(params string[] lines)
    {
        Section("Diagnostic status");
        foreach (var line in lines)
            if (!string.IsNullOrWhiteSpace(line)) ImGui.TextWrapped(line);
    }
    internal void Draw(string context, Action close, Action stop, Action<Desk> drawPage, Action<Vector2, float> drawMark)
    {
        float s = Scale;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.082f,.087f,.11f,1));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(.12f,.34f,.34f,1));
        try
        {
            var pos = ImGui.GetCursorScreenPos();
            var dl = ImGui.GetWindowDrawList();
            drawMark(pos,30*s);
            float width = ImGui.GetContentRegionAvail().X;
            var text = pos + new Vector2(38*s,(30*s-ImGui.GetTextLineHeight())/2);
            dl.AddText(text,0xfff2f2f4,"Steward");
            dl.AddText(text+new Vector2(ImGui.CalcTextSize("Steward").X+12*s,0),0xffc8c4bb,context);
            ImGui.InvisibleButton("Move Steward",new(Math.Max(1,width-40*s),30*s));
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                ImGui.SetWindowPos(ImGui.GetWindowPos()+ImGui.GetIO().MouseDelta);
            ImGui.SameLine();
            if (ImGui.Button("×###Close Steward",new(28*s,30*s))) close();
            ImGui.Separator();
            var bodyHeight=Math.Max(1,ImGui.GetContentRegionAvail().Y-ImGui.GetFrameHeightWithSpacing()-ImGui.GetStyle().ItemSpacing.Y);
            float railButtonHeight = Math.Clamp((bodyHeight - (Rail.Length-1)*ImGui.GetStyle().ItemSpacing.Y)/Rail.Length,28*s,41*s);
            if (ImGui.BeginChild("Rail",new(48*s,bodyHeight),false))
            {
                foreach(var destination in Rail)
                {
                    int i=(int)destination;
                    bool active=!results && Area(Current)==destination;
                    var p=ImGui.GetCursorScreenPos();
                    ImGui.PushStyleColor(ImGuiCol.Button,active?new Vector4(.12f,.29f,.30f,1):Vector4.Zero);
                    if (ImGui.Button("###"+Names[i],new(42*s,railButtonHeight))) { Current=destination; history.Clear(); results=false; search=""; }
                    ImGui.PopStyleColor();
                    if (active) ImGui.GetWindowDrawList().AddRectFilled(p,p+new Vector2(3*s,railButtonHeight),0xffb9d960);
                    Glyph(i,p+new Vector2(9*s,(railButtonHeight-24*s)/2),s,active?0xffd6e582:0xffcdc6bc);
                    if(ImGui.IsItemHovered()||ImGui.IsItemFocused()) ImGui.SetTooltip(Names[i]);
                }
            }
            ImGui.EndChild(); ImGui.SameLine();
            var gutter=(ImGui.GetWindowSize().X<600*s ? new Vector2(10,9) : new Vector2(17,14))*s;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,gutter);
            bool workspaceVisible=ImGui.BeginChild("Workspace",new(0,bodyHeight),false,ImGuiWindowFlags.AlwaysUseWindowPadding);
            ImGui.PopStyleVar();
            if(workspaceVisible)
            {
                if(history.TryPeek(out var parent) && !results)
                {
                    if(ImGui.SmallButton(parent.Results?"Back to results":"Back to "+Names[(int)parent.Page]))
                    { (Current,results)=history.Pop(); }
                }
                if(Current==Desk.Setup || results)
                {
                    ImGui.SetNextItemWidth(-1);
                    if(ImGui.InputTextWithHint("##FindPage","Find a setting...",ref search,100))
                        results=search.Trim().Length>0;
                }
                if(results)
                {
                    bool any=false;
                    for(int i=0;i<Names.Length;i++)
                        if((Names[i]+" "+Topics[i]).Contains(search.Trim(),StringComparison.OrdinalIgnoreCase))
                        { if(ImGui.Selectable(Names[i])) Open((Desk)i); any=true; }
                    if(!any) ImGui.TextWrapped("No matching pages.");
                }
                else if(ImGui.BeginChild("Page"+Current))
                {
                    ImGui.TextUnformatted(Names[(int)Current]);
                    ImGui.Separator();
                    ImGui.PushItemWidth(Math.Min(240*s,ImGui.GetContentRegionAvail().X*.42f));
                    drawPage(Current);
                    ImGui.PopItemWidth();
                    ImGui.EndChild();
                }
                else ImGui.EndChild();
            }
            ImGui.EndChild();
            ImGui.Separator();
            if(ImGui.Button("Stop current Steward run")) stop();
        }
        finally { ImGui.PopStyleColor(2); }
    }

    // A pulse, price tag, hanger, box, list, link, terminal and trend line.
    private static void Glyph(int i, Vector2 p, float s, uint ink)
    {
        var dl=ImGui.GetWindowDrawList();
        Vector2 P(int x,int y)=>p+new Vector2(x,y)*s;
        void Path(params (int X,int Y)[] points)
        {
            for(int j=1;j<points.Length;j++)
                dl.AddLine(P(points[j-1].X,points[j-1].Y),P(points[j].X,points[j].Y),ink,1.8f*s);
        }
        switch((Desk)i)
        {
            case Desk.Activity: Path((2,13),(7,13),(10,4),(14,20),(17,11),(22,11));break;
            case Desk.Market: case Desk.Sales: Path((3,4),(13,4),(22,13),(13,22),(3,12),(3,4));dl.AddCircleFilled(P(8,8),1.6f*s,ink);break;
            case Desk.Collection: case Desk.Glamour: Path((9,7),(10,4),(14,4),(16,7),(12,11),(12,12),(22,18),(22,20),(2,20),(2,18),(12,12));break;
            case Desk.Storage: Path((3,8),(12,4),(21,8),(21,18),(12,22),(3,18),(3,8),(12,12),(21,8));Path((12,12),(12,22));break;
            case Desk.Inventory: case Desk.Items: for(int y=6;y<=18;y+=6){dl.AddCircleFilled(P(4,y),1.5f*s,ink);Path((9,y),(21,y));}break;
            case Desk.Setup: case Desk.Connections: Path((9,15),(6,18),(3,15),(4,12),(9,7),(13,8));Path((11,16),(15,17),(21,11),(21,8),(18,5),(15,8));Path((9,15),(15,9));break;
            case Desk.Review: Path((3,4),(21,4),(21,20),(3,20),(3,4));Path((6,9),(10,12),(6,15));Path((13,15),(18,15));break;
            case Desk.Strategies: Path((3,18),(9,12),(13,15),(21,6));Path((15,6),(21,6),(21,12));break;
        }
    }
}
