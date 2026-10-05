using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace UsageRings {
 internal static class Values {
  internal static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
  internal static object At(object value, params string[] path) {
   foreach(var name in path) {
    var dictionary=value as Dictionary<string,object>;
    if(dictionary==null || !dictionary.ContainsKey(name)) return null;
    value=dictionary[name];
   }
   return value;
  }
  internal static double? Number(object value) {
   if(value==null) return null;
   double result;
   return Double.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture), NumberStyles.Float,
    CultureInfo.InvariantCulture,out result) && !Double.IsNaN(result) && !Double.IsInfinity(result) ? (double?)result : null;
  }
  internal static double? NumberAt(object value, params string[] path) { return Number(At(value,path)); }
  internal static string Text(object value) { return value==null ? null : Convert.ToString(value,CultureInfo.InvariantCulture); }
  internal static string Tokens(double? value) {
   if(!value.HasValue) return "—";
   return value.Value>=1000000 ? (value.Value/1000000).ToString("0.##")+"M"
    : value.Value>=1000 ? (value.Value/1000).ToString("0.#")+"k" : value.Value.ToString("0");
  }
  internal static string Percent(double? value) { return value.HasValue ? Math.Floor(value.Value).ToString("0",CultureInfo.InvariantCulture)+"%" : "—"; }
  internal static Color ColorAt(object value, string name, Color fallback) {
   try { var text=Text(At(value,name)); return text==null ? fallback : ColorTranslator.FromHtml(text); }
   catch { return fallback; }
  }
 }

 internal static class NativeWindows {
  [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPointer(IntPtr window,int index);
  internal static int LastPositionError;
  internal static bool IsTopmost(Form form) { return (GetWindowLongPointer(form.Handle,-20).ToInt64()&8)!=0; }
  internal static bool Position(Form form,ButtonAnchor anchor) {
   bool alreadyTopmost=IsTopmost(form);
   uint flags=0x0010|0x0040|0x0200;
   var center=new Point(anchor.ButtonBounds.Left+anchor.ButtonBounds.Width/2,
    anchor.ButtonBounds.Top+anchor.ButtonBounds.Height/2);
   var hitWindow=NativeAnchor.WindowFromPoint(center);
   bool coveredByHost=hitWindow!=IntPtr.Zero&&NativeAnchor.GetAncestor(hitWindow,2)==anchor.Window;
   if(alreadyTopmost&&!coveredByHost) flags|=0x0004;
   bool positioned=SetWindowPos(form.Handle,new IntPtr(-1),anchor.ButtonBounds.Left,anchor.ButtonBounds.Top,
    anchor.ButtonBounds.Width,anchor.ButtonBounds.Height,flags);
   LastPositionError=positioned?0:Marshal.GetLastWin32Error();
   return positioned;
  }
 }

 internal sealed class RingWindow : Form {
  internal object Snapshot;
  internal Action ToggleDetails;
  internal RingWindow() {
   Text="Codex Usage Rings";
   FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual;
   AutoScaleMode=AutoScaleMode.None; DoubleBuffered=true; BackColor=Color.FromArgb(54,54,54);
   Size=new Size(32,32); Cursor=Cursors.Hand;
   var tooltip=new ToolTip(); tooltip.SetToolTip(this,"Click to view context and account usage");
   MouseClick+=delegate(object sender,MouseEventArgs args) { if(args.Button==MouseButtons.Left&&ToggleDetails!=null) ToggleDetails(); };
  }
  protected override bool ShowWithoutActivation { get { return true; } }
  protected override CreateParams CreateParams { get { var parameters=base.CreateParams; parameters.ExStyle|=0x80|0x08000000; return parameters; } }
  internal static void PaintRings(Graphics graphics,Rectangle bounds,object snapshot) {
   graphics.SmoothingMode=SmoothingMode.AntiAlias;
   float scale=Math.Min(bounds.Width,bounds.Height)/32f;
   float centerX=bounds.Left+bounds.Width/2f, centerY=bounds.Top+bounds.Height/2f;
   var outer=Values.NumberAt(snapshot,"context","percent");
   if(outer.HasValue&&outer.Value>0) {
    using(var pen=new Pen(Values.ColorAt(snapshot,"contextColor",Color.FromArgb(71,151,237)),2*scale)) {
     var rectangle=new RectangleF(centerX-11*scale,centerY-11*scale,22*scale,22*scale);
     if(outer.Value>=100) graphics.DrawEllipse(pen,rectangle);
     else { pen.StartCap=LineCap.Round; pen.EndCap=LineCap.Round; graphics.DrawArc(pen,rectangle,-90,(float)Math.Min(100,outer.Value)*3.6f); }
    }
   }
   var inner=Values.NumberAt(snapshot,"quota","usedPercent");
   if(inner.HasValue&&inner.Value>0) {
    using(var brush=new SolidBrush(Values.ColorAt(snapshot,"quotaColor",Color.FromArgb(90,189,123)))) {
     var rectangle=new RectangleF(centerX-6*scale,centerY-6*scale,12*scale,12*scale);
     if(inner.Value>=100) graphics.FillEllipse(brush,rectangle);
     else graphics.FillPie(brush,rectangle.X,rectangle.Y,rectangle.Width,rectangle.Height,-90,(float)Math.Min(100,inner.Value)*-3.6f);
    }
   }
  }
  protected override void OnPaint(PaintEventArgs args) { base.OnPaint(args); PaintRings(args.Graphics,ClientRectangle,Snapshot); }
 }

 internal sealed class DetailsWindow : Form {
  internal object Snapshot;
  internal double DisplayScale=1;
  internal Color ComposerBackground=Color.FromArgb(54,54,54);
  private readonly PositionSwitch positionSwitch=new PositionSwitch();
  private readonly ToolTip tooltip=new ToolTip();
  internal event EventHandler PositionChanged;
  internal bool ModelAvailable=true;
  internal string PreferenceError;
  internal bool PlaceBesideModel { get { return positionSwitch.Checked; } set { positionSwitch.Checked=value; } }
  internal const int LogicalWidth=400;
  internal const int ContentWidth=LogicalWidth-32;
  internal const int StandardHeight=226;
  internal const int QuotaRowHeight=57;
  internal int LogicalHeight { get { return StandardHeight-(QuotaWindow("hourly")==null?QuotaRowHeight:0); } }
  private object QuotaWindow(string kind) {
   var windows=Values.At(Snapshot,"quota","windows") as object[];
   if(windows!=null) foreach(var window in windows)
    if(Values.Text(Values.At(window,"kind"))==kind) return window;
   return null;
  }
  internal DetailsWindow() {
   Text="Codex Usage Details"; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false;
   StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.None; DoubleBuffered=true;
   Controls.Add(positionSwitch);
   tooltip.SetToolTip(positionSwitch,"Left: beside model. Right: cover microphone.");
   positionSwitch.CheckedChanged+=delegate { if(PositionChanged!=null) PositionChanged(this,EventArgs.Empty); };
   Deactivate+=delegate { Hide(); };
   KeyDown+=delegate(object sender,KeyEventArgs args) { if(args.KeyCode==Keys.Escape) Hide(); };
  }
  private static void TextAt(Graphics graphics,string text,Font font,Color color,float x,float y,float width,bool right) {
   using(var brush=new SolidBrush(color)) {
    var format=new StringFormat(); format.Alignment=right?StringAlignment.Far:StringAlignment.Near;
    format.FormatFlags=StringFormatFlags.NoWrap; format.Trimming=StringTrimming.EllipsisCharacter;
    graphics.DrawString(text,font,brush,new RectangleF(x,y,width,30),format);
    format.Dispose();
   }
  }
  private static void DrawBar(Graphics graphics,float x,float y,float width,double? percent,Color color,Color track) {
   using(var background=new SolidBrush(track)) graphics.FillRectangle(background,x,y,width,5);
   if(percent.HasValue&&percent.Value>0) using(var fill=new SolidBrush(color))
    graphics.FillRectangle(fill,x,y,width*(float)Math.Min(100,Math.Max(0,percent.Value))/100f,5);
  }
  internal static string ResetDescription(object window,DateTimeOffset now,TimeZoneInfo zone) {
   var seconds=Values.NumberAt(window,"resetsAt");
   if(!seconds.HasValue) return "Reset time unavailable";
   try {
    var reset=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(seconds.Value);
    var remaining=reset-now;
    string countdown;
    if(remaining.TotalSeconds<=0) countdown="due";
    else if(remaining.TotalDays>=1) countdown=((int)remaining.TotalDays).ToString(CultureInfo.InvariantCulture)
      +" d "+remaining.Hours.ToString(CultureInfo.InvariantCulture)+" h";
    else if(remaining.TotalHours>=1) countdown=((int)remaining.TotalHours).ToString(CultureInfo.InvariantCulture)+" h";
    else if(remaining.TotalMinutes>=1) countdown=((int)remaining.TotalMinutes).ToString(CultureInfo.InvariantCulture)+" m";
    else countdown="<1 m";
    return "Reset at "+TimeZoneInfo.ConvertTime(reset,zone).ToString("MMM d HH:mm",CultureInfo.InvariantCulture)
      +" ("+countdown+")";
   } catch { return "Reset time unavailable"; }
  }
  internal static string CreditsDescription(object snapshot) {
   if(Values.At(snapshot,"credits","unlimited") is bool&&(bool)Values.At(snapshot,"credits","unlimited"))
    return "Credits remaining: Unlimited";
   var balance=Values.NumberAt(snapshot,"credits","balance");
   return "Credits remaining: "+(balance.HasValue?balance.Value.ToString("#,0.##",CultureInfo.InvariantCulture):"—");
  }
  internal static string ContextDescription(object snapshot) {
   var context=Values.At(snapshot,"context");
   if(context!=null) return Values.Tokens(Values.NumberAt(context,"usedTokens"))+" / "+
    Values.Tokens(Values.NumberAt(context,"capacityTokens"))+" ("+Values.Percent(Values.NumberAt(context,"percent"))+")";
   switch(Values.Text(Values.At(snapshot,"contextStatus"))) {
    case "not-yet-reported": return "Waiting for first usage";
    case "compacted": return "Waiting for usage update";
    case "unbound": return "Conversation not confirmed";
    default: return "Unavailable";
   }
  }
  internal void DrawContents(Graphics graphics,Rectangle bounds) {
   int rowOffset=LogicalHeight-StandardHeight;
   bool dark=ComposerBackground.GetBrightness()<0.5;
   var background=dark?Color.FromArgb(34,34,34):Color.FromArgb(250,250,250);
   var foreground=dark?Color.FromArgb(230,230,230):Color.FromArgb(35,35,35);
   var secondary=dark?Color.FromArgb(150,150,150):Color.FromArgb(110,110,110);
   var track=dark?Color.FromArgb(63,63,63):Color.FromArgb(225,225,225);
   graphics.Clear(background); graphics.SmoothingMode=SmoothingMode.AntiAlias;
   positionSwitch.DisplayScale=DisplayScale; positionSwitch.BackColor=background;
   string contextNotice=Values.At(Snapshot,"context")==null
    ?Values.Text(Values.At(Snapshot,"contextStatus"))=="compacted"
      ?"Context was compacted. Waiting for the next usage update."
      :"Waiting for a verified usage update from the main conversation."
    :null;
   string quotaNotice=Values.Text(Values.At(Snapshot,"quotaSource"))=="session"
    ?"Account limits show the most recent recorded usage; live account data is unavailable.":null;
   tooltip.SetToolTip(this,String.Join(" ",new[]{contextNotice,quotaNotice}).Trim());
   tooltip.SetToolTip(positionSwitch,PreferenceError??(PlaceBesideModel&&!ModelAvailable
    ?"Model button unavailable; using microphone.":"Left: beside model. Right: cover microphone."));
   positionSwitch.Bounds=new Rectangle((int)Math.Round((LogicalWidth-162)*DisplayScale),(int)Math.Round((198+rowOffset)*DisplayScale),
    (int)Math.Round(146*DisplayScale),(int)Math.Round(24*DisplayScale));
   graphics.ScaleTransform((float)DisplayScale,(float)DisplayScale);
   using(var body=new Font("Segoe UI",13,FontStyle.Regular,GraphicsUnit.Pixel))
   using(var heading=new Font("Segoe UI",13,FontStyle.Bold,GraphicsUnit.Pixel))
   using(var small=new Font("Segoe UI",11,FontStyle.Regular,GraphicsUnit.Pixel)) {
    var context=Values.At(Snapshot,"context");
    var contextPercent=Values.NumberAt(context,"percent");
    var contextColor=Values.ColorAt(Snapshot,"contextColor",Color.FromArgb(71,151,237));
    TextAt(graphics,"Context window",body,secondary,16,15,140,false);
    string contextText=ContextDescription(Snapshot);
    TextAt(graphics,contextText,body,secondary,120,15,LogicalWidth-136,true);
    DrawBar(graphics,16,43,ContentWidth,contextPercent,contextColor,track);
    using(var line=new Pen(track)) graphics.DrawLine(line,16,62,LogicalWidth-16,62);
    var hourly=QuotaWindow("hourly"); var weekly=QuotaWindow("weekly");
    if(hourly!=null) DrawQuota(graphics,hourly,"5-hour limit",76,body,heading,small,foreground,secondary,track);
    DrawQuota(graphics,weekly,"Weekly limit",133+rowOffset,body,heading,small,foreground,secondary,track);
    using(var line=new Pen(track)) graphics.DrawLine(line,16,191+rowOffset,LogicalWidth-16,191+rowOffset);
    TextAt(graphics,CreditsDescription(Snapshot),small,secondary,16,202+rowOffset,LogicalWidth-182,false);
    PositionSwitch.PaintSwitch(graphics,new RectangleF(LogicalWidth-162,198+rowOffset,146,24),PlaceBesideModel,dark);
      }
   graphics.ResetTransform();
   using(var border=new Pen(dark?Color.FromArgb(65,65,65):Color.FromArgb(205,205,205)))
    graphics.DrawRectangle(border,0,0,bounds.Width-1,bounds.Height-1);
  }
  private static void DrawQuota(Graphics graphics,object window,string label,float y,Font body,Font heading,Font small,
    Color foreground,Color secondary,Color track) {
   var percent=Values.NumberAt(window,"usedPercent");
   var color=!percent.HasValue||percent.Value<70?Color.FromArgb(90,189,123)
    :percent.Value<=85?Color.FromArgb(238,179,71):Color.FromArgb(239,102,102);
   TextAt(graphics,label,heading,foreground,16,y,LogicalWidth-100,false);
   TextAt(graphics,Values.Percent(percent),body,foreground,LogicalWidth-72,y,56,true);
   string resetText=ResetDescription(window,DateTimeOffset.UtcNow,TimeZoneInfo.Local);
   string forecastText=Values.Text(Values.At(window,"forecast","text"))??String.Empty;
   bool hasForecast=Values.Text(Values.At(window,"kind"))=="weekly"&&!String.IsNullOrEmpty(forecastText);
   float forecastWidth=hasForecast?graphics.MeasureString(forecastText,small).Width+2:0;
   float resetWidth=ContentWidth-(hasForecast?forecastWidth+8:0);
   TextAt(graphics,resetText,small,secondary,16,y+20,resetWidth,false);
   if(hasForecast) TextAt(graphics,forecastText,small,secondary,LogicalWidth-16-forecastWidth,y+20,forecastWidth,true);
   DrawBar(graphics,16,y+40,ContentWidth,percent,color,track);
  }
  protected override void OnPaint(PaintEventArgs args) { base.OnPaint(args); DrawContents(args.Graphics,ClientRectangle); }
 }

 internal sealed class WidgetContext : ApplicationContext {
  private readonly RingWindow ring=new RingWindow();
  private readonly DetailsWindow details=new DetailsWindow();
  private readonly DetailsPlacement detailsPlacement=new DetailsPlacement();
  private readonly NotifyIcon tray=new NotifyIcon();
  private readonly ManualResetEvent stopped=new ManualResetEvent(false);
  private readonly object anchorGate=new object();
  private readonly AnchorTracker anchorTracker=new AnchorTracker();
  private readonly string root;
  private readonly string statusPath;
  private readonly WidgetPreferences preferences;
  private Process bridge;
  private StreamWriter bridgeInput;
  private ButtonAnchor verifiedAnchor;
  private object snapshot;
  private int windowCount;
  private string verifiedPageTitle, currentPageTitle;
  private bool pageObserved;
  private long pageRevision;
  private int trackingFailures;
  private DateTime lastDataRequest=DateTime.MinValue,lastStatus=DateTime.MinValue,lastThemeSample=DateTime.MinValue;
  private readonly System.Windows.Forms.Timer followTimer=new System.Windows.Forms.Timer();

  internal WidgetContext(string node,string initialThreadId) {
   root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",".."));
   statusPath=Path.Combine(root,"_Claude_tmp","Native_Widget_State_Private.json");
   preferences=new WidgetPreferences(root); details.PlaceBesideModel=preferences.PlaceBesideModel;
   details.PreferenceError=preferences.Error;
   details.PositionChanged+=delegate {
    preferences.Save(details.PlaceBesideModel); details.PreferenceError=preferences.Error;
    detailsPlacement.SelectionChanged(); FollowWindow(); details.Invalidate();
   };
   ring.ToggleDetails=ToggleDetails;
   var ringHandle=ring.Handle;
   var menu=new ContextMenuStrip();
   var open=new ToolStripMenuItem("View usage details"); open.Click+=delegate { ToggleDetails(); };
   var exit=new ToolStripMenuItem("Exit usage rings"); exit.Click+=delegate { ExitThread(); };
   menu.Items.Add(open); menu.Items.Add(exit);
   ring.ContextMenuStrip=menu;
   tray.Icon=SystemIcons.Information; tray.Text="Codex Usage Rings"; tray.ContextMenuStrip=menu; tray.Visible=true;
   tray.DoubleClick+=delegate { ToggleDetails(); };
   StartBridge(node,initialThreadId);
   var worker=new Thread(TrackAnchor); worker.IsBackground=true; worker.SetApartmentState(ApartmentState.MTA); worker.Start();
   followTimer.Interval=100; followTimer.Tick+=delegate { FollowWindow(); }; followTimer.Start();
  }

  private static string Quote(string value) { return "\""+value.Replace("\"","\\\"")+"\""; }
  internal static StreamWriter CreateBridgeInput(Stream stream) {
   return new StreamWriter(stream,new UTF8Encoding(false)) { AutoFlush=true };
  }
  private void StartBridge(string node,string threadId) {
   var arguments=Quote(Path.Combine(root,"native","bridge.mjs"));
   if(!String.IsNullOrEmpty(threadId)) arguments+=" --thread-id "+Quote(threadId);
   bridge=new Process { StartInfo=new ProcessStartInfo(node,arguments) {
    UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=true, RedirectStandardOutput=true,
    RedirectStandardError=true, StandardOutputEncoding=Encoding.UTF8, StandardErrorEncoding=Encoding.UTF8,
    WorkingDirectory=root
   }, EnableRaisingEvents=true };
   bridge.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs args) {
    if(String.IsNullOrEmpty(args.Data)) return;
    try {
     var value=new JavaScriptSerializer().DeserializeObject(args.Data);
     ring.BeginInvoke(new Action(delegate {
      if(Values.NumberAt(value,"pageRevision")!=pageRevision) return;
      var previous=Values.Text(Values.At(snapshot,"threadId"));
      snapshot=value; ring.Snapshot=value; details.Snapshot=value;
      if(previous!=Values.Text(Values.At(value,"threadId"))) details.Hide();
      ring.Invalidate(); details.Invalidate();
     }));
    } catch { }
   };
   bridge.ErrorDataReceived+=delegate { };
   bridge.Start(); bridgeInput=CreateBridgeInput(bridge.StandardInput.BaseStream);
   bridge.BeginOutputReadLine(); bridge.BeginErrorReadLine();
  }

  private void TrackAnchor() {
   while(!stopped.WaitOne(1)) {
    try {
     var windows=NativeAnchor.FindWindows(); windowCount=windows.Count;
     IntPtr target=IntPtr.Zero;
     var foreground=NativeAnchor.GetForegroundWindow();
     foreach(var window in windows) if(window==foreground) { target=window; break; }
     lock(anchorGate) {
      if(target==IntPtr.Zero&&verifiedAnchor!=null&&windows.Contains(verifiedAnchor.Window)) target=verifiedAnchor.Window;
     }
     if(target==IntPtr.Zero&&windows.Count==1) target=windows[0];
     string pageTitle=null;
     if(target!=IntPtr.Zero&&!NativeAnchor.IsIconic(target)) {
      try { pageTitle=NativeAnchor.ReadPageTitle(target); } catch { }
     }
     lock(anchorGate) { verifiedPageTitle=pageTitle; }
     if(target==IntPtr.Zero||NativeAnchor.IsIconic(target)||foreground!=target) {
      if(stopped.WaitOne(600)) return;
      continue;
     }
     ButtonAnchor next=null;
     if(target!=IntPtr.Zero&&!NativeAnchor.IsIconic(target)) {
      ButtonAnchor composer=null;
      try { next=NativeAnchor.FindButton(target,null,out composer); } catch { }
      if(next==null) next=NativeVision.FindButton(target,composer);
     }
     next=anchorTracker.Update(next);
     lock(anchorGate) { verifiedAnchor=next; verifiedPageTitle=pageTitle; }
     trackingFailures=next==null?trackingFailures+1:0;
    } catch { anchorTracker.Update(null); lock(anchorGate) { verifiedAnchor=null; } trackingFailures++; }
    if(stopped.WaitOne(1200)) return;
   }
  }

  private void FollowWindow() {
   ButtonAnchor anchor;
   string pageTitle;
   lock(anchorGate) { anchor=verifiedAnchor; pageTitle=verifiedPageTitle; }
   if(!pageObserved||pageTitle!=currentPageTitle) {
    pageObserved=true; currentPageTitle=pageTitle; pageRevision++;
    var previous=snapshot as Dictionary<string,object>;
    var cleared=previous==null?new Dictionary<string,object>():new Dictionary<string,object>(previous);
    cleared["context"]=null; cleared["threadId"]=null; cleared["selectedThreadId"]=null;
    cleared["contextStatus"]="unbound"; cleared["status"]="unbound";
    cleared["warnings"]=new string[] { "Current page changed; context usage is unknown until its conversation is confirmed." };
    snapshot=cleared; ring.Snapshot=cleared; details.Snapshot=cleared;
    details.Hide(); ring.Invalidate(); lastDataRequest=DateTime.MinValue;
   }
   bool visible=false;
   if(anchor!=null&&!NativeAnchor.IsIconic(anchor.Window)&&NativeAnchor.IsWindowVisible(anchor.Window)) {
    NativeAnchor.NativeRectangle currentBounds;
    if(NativeAnchor.GetWindowRect(anchor.Window,out currentBounds)) {
     var bounds=currentBounds.ToRectangle();
     var foreground=NativeAnchor.GetForegroundWindow();
     bool allowed=foreground==anchor.Window||foreground==ring.Handle||foreground==details.Handle;
     if(allowed&&bounds.Size==anchor.WindowBounds.Size) {
      var button=anchor.ButtonBounds; button.Offset(bounds.Left-anchor.WindowBounds.Left,bounds.Top-anchor.WindowBounds.Top);
      var model=anchor.ModelBounds; var toolbar=anchor.ToolbarBounds;
      if(!model.IsEmpty) model.Offset(bounds.Left-anchor.WindowBounds.Left,bounds.Top-anchor.WindowBounds.Top);
      if(!toolbar.IsEmpty) toolbar.Offset(bounds.Left-anchor.WindowBounds.Left,bounds.Top-anchor.WindowBounds.Top);
      var current=new ButtonAnchor { Window=anchor.Window,WindowBounds=bounds,ButtonBounds=button,Scale=anchor.Scale,
       ButtonName=anchor.ButtonName,ModelBounds=model,ToolbarBounds=toolbar };
      details.ModelAvailable=!model.IsEmpty;
      if((DateTime.UtcNow-lastThemeSample).TotalSeconds>=0.8) {
       lastThemeSample=DateTime.UtcNow;
       var background=NativeAnchor.ReadBackground(current,ring.BackColor);
       ring.BackColor=background; details.ComposerBackground=background;
       if(details.Visible) details.Invalidate();
      }
      current=NativeAnchor.ForPlacement(current,details.PlaceBesideModel);
      if(!ring.Visible) ring.Show();
      bool positioned=NativeWindows.Position(ring,current);
      if(details.Visible) PositionDetails(detailsPlacement.FollowWindow(bounds));
      visible=positioned&&NativeAnchor.IsWindowVisible(ring.Handle);
     }
    }
   }
   if(!visible) { ring.Hide(); details.Hide(); }
   if((DateTime.UtcNow-lastDataRequest).TotalSeconds>=5) {
    lastDataRequest=DateTime.UtcNow;
    try { if(!bridge.HasExited) bridgeInput.WriteLine(Values.Serializer.Serialize(new { type="refresh",windowCount=windowCount,pageTitle=currentPageTitle,pageRevision=pageRevision })); } catch { }
   }
   if((DateTime.UtcNow-lastStatus).TotalSeconds>=2) {
    lastStatus=DateTime.UtcNow;
    try {
     Directory.CreateDirectory(Path.GetDirectoryName(statusPath));
     var buttonPoint=new Point(ring.Left+ring.Width/2,ring.Top+ring.Height/2);
     var hitWindow=visible?NativeAnchor.WindowFromPoint(buttonPoint):IntPtr.Zero;
     var hitRoot=hitWindow==IntPtr.Zero?IntPtr.Zero:NativeAnchor.GetAncestor(hitWindow,2);
     File.WriteAllText(statusPath,Values.Serializer.Serialize(new {
      updatedAt=DateTime.UtcNow.ToString("o"),visible=visible,detailsVisible=details.Visible,windowCount=windowCount,
      anchor=anchor==null?null:anchor.ButtonBounds.ToString(), background=ColorTranslator.ToHtml(ring.BackColor),
      threadId=Values.Text(Values.At(snapshot,"threadId")),
      pageRevision=pageRevision,
      contextTokens=Values.NumberAt(snapshot,"context","usedTokens"),
      contextStatus=Values.Text(Values.At(snapshot,"contextStatus")),bindingSource=Values.Text(Values.At(snapshot,"bindingSource")),
      selectedThreadId=Values.Text(Values.At(snapshot,"selectedThreadId")),dataStatus=Values.Text(Values.At(snapshot,"status")),
      quotaUsedPercent=Values.NumberAt(snapshot,"quota","usedPercent"), trackingFailures=trackingFailures,
      foreground=NativeAnchor.GetForegroundWindow().ToInt64(), recognitionScore=NativeVision.LastScore,
      placement=details.PlaceBesideModel?"model":"microphone",displayAnchor=ring.Bounds.ToString(),
      modelAnchor=anchor==null?null:anchor.ModelBounds.ToString(),
      backgroundSamples=NativeTheme.AcceptedSamples,occludedSamples=NativeTheme.OccludedSamples,
      overlayWindow=ring.Handle.ToInt64(),codexWindow=anchor==null?0:anchor.Window.ToInt64(),
      buttonHitWindow=hitWindow.ToInt64(),buttonHitRoot=hitRoot.ToInt64(),buttonExposed=visible&&hitRoot==ring.Handle,
      positionError=NativeWindows.LastPositionError,overlayTopmost=NativeWindows.IsTopmost(ring)
     }),new UTF8Encoding(false));
    } catch { }
   }
  }

  private void PositionDetails(ButtonAnchor anchor) {
   double scale=anchor.Scale;
   details.DisplayScale=scale;
   details.Size=new Size((int)Math.Round(DetailsWindow.LogicalWidth*scale),(int)Math.Round(details.LogicalHeight*scale));
   int margin=(int)Math.Round(8*scale);
   int x=anchor.ButtonBounds.Right-details.Width;
   int y=anchor.ButtonBounds.Top-margin-details.Height;
   x=Math.Max(anchor.WindowBounds.Left+margin,Math.Min(x,anchor.WindowBounds.Right-margin-details.Width));
   y=Math.Max(anchor.WindowBounds.Top+margin,y);
   details.Location=new Point(x,y);
  }

  private void ToggleDetails() {
   if(details.Visible&&!detailsPlacement.HasPendingChange) { details.Hide(); return; }
   ButtonAnchor anchor; lock(anchorGate) { anchor=verifiedAnchor; }
   if(anchor==null||!ring.Visible) return;
   details.Snapshot=snapshot; details.ComposerBackground=ring.BackColor;
   var opening=NativeAnchor.ForPlacement(anchor,details.PlaceBesideModel);
   detailsPlacement.Capture(opening); PositionDetails(opening);
   if(!details.Visible) details.Show(ring); details.Activate(); details.Invalidate();
  }

  protected override void ExitThreadCore() {
   stopped.Set(); followTimer.Stop(); tray.Visible=false; tray.Dispose(); details.Close(); ring.Close();
   if(bridge!=null) {
    try { bridgeInput.Close(); if(!bridge.WaitForExit(2500)) bridge.Kill(); } catch { }
    bridge.Dispose();
   }
   base.ExitThreadCore();
  }
 }

 internal static class Program {
  [STAThread]
  public static int Main(string[] args) {
   try { NativeAnchor.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
   if(args.Length>0&&args[0]=="--render-test") return RenderTest(args[1]);
   bool created;
   using(var mutex=new Mutex(true,"Local\\CodexUsageRings.Native."+Environment.UserName,out created)) {
    if(!created) return 0;
    string node="node",threadId=null;
    for(int index=0;index<args.Length-1;index++) {
     if(args[index]=="--node") node=args[++index];
     else if(args[index]=="--thread-id") threadId=args[++index];
    }
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
    try { Application.Run(new WidgetContext(node,threadId)); return 0; }
    catch(Exception error) {
     MessageBox.Show("Could not start usage rings: "+error.Message,"Codex Usage Rings",MessageBoxButtons.OK,MessageBoxIcon.Error);
     return 1;
    }
   }
  }

  private static int RenderTest(string directory) {
   Directory.CreateDirectory(directory);
   var state=Values.Serializer.DeserializeObject("{\"threadId\":\"11111111-2222-3333-4444-555555555555\",\"context\":{\"usedTokens\":384300,\"capacityTokens\":1000000,\"percent\":38.43},\"contextColor\":\"#eeb347\",\"quotaColor\":\"#ef6666\",\"quota\":{\"usedPercent\":94,\"selectedKind\":\"weekly\",\"windows\":[{\"kind\":\"hourly\",\"usedPercent\":11,\"resetsAt\":2000000000},{\"kind\":\"weekly\",\"usedPercent\":94,\"resetsAt\":2000100000}]},\"updatedAt\":\"2026-10-05T12:00:00Z\"}");
   using(var image=new Bitmap(96,96)) {
    using(var graphics=Graphics.FromImage(image)) { graphics.Clear(Color.FromArgb(54,54,54)); RingWindow.PaintRings(graphics,new Rectangle(0,0,96,96),state); }
    image.Save(Path.Combine(directory,"native-rings.png"),ImageFormat.Png);
    if(image.GetPixel(49,49).R<200) throw new Exception("The filled inner circle does not cover its center.");
   }
   using(var image=new Bitmap(48,48)) {
    using(var graphics=Graphics.FromImage(image)) { graphics.Clear(Color.Transparent); RingWindow.PaintRings(graphics,new Rectangle(0,0,48,48),Values.Serializer.DeserializeObject("{\"context\":{\"percent\":0},\"quota\":{\"usedPercent\":0}}")); }
    for(int y=0;y<48;y++) for(int x=0;x<48;x++) if(image.GetPixel(x,y).A!=0) throw new Exception("Zero usage must remain transparent.");
   }
   using(var image=new Bitmap(96,96)) {
    var quarter=Values.Serializer.DeserializeObject("{\"context\":{\"percent\":25},\"contextColor\":\"#4797ed\",\"quota\":{\"usedPercent\":25},\"quotaColor\":\"#5abd7b\"}");
    using(var graphics=Graphics.FromImage(image)) {
     graphics.Clear(Color.Transparent); RingWindow.PaintRings(graphics,new Rectangle(0,0,96,96),quarter);
    }
    if(image.GetPixel(40,40).G<150 || image.GetPixel(56,40).A!=0)
     throw new Exception("The inner circle must fill counterclockwise from the top.");
    image.Save(Path.Combine(directory,"native-rings-quarter.png"),ImageFormat.Png);
   }
   using(var details=new DetailsWindow()) {
    details.Snapshot=state; details.DisplayScale=1.5;
    foreach(bool dark in new[]{true,false}) {
     details.ComposerBackground=dark?Color.FromArgb(54,54,54):Color.FromArgb(240,240,240);
     using(var image=new Bitmap((int)Math.Round(DetailsWindow.LogicalWidth*1.5),(int)Math.Round(details.LogicalHeight*1.5))) {
      using(var graphics=Graphics.FromImage(image)) details.DrawContents(graphics,new Rectangle(0,0,image.Width,image.Height));
      image.Save(Path.Combine(directory,dark?"native-details-dark.png":"native-details-light.png"),ImageFormat.Png);
     }
    }
   }
   return 0;
  }
 }
}
