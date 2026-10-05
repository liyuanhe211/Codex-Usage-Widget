using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace UsageRings {
 internal static class NativeTheme {
  internal static int AcceptedSamples;
  internal static int OccludedSamples;

  internal static Rectangle SamplingBounds(ButtonAnchor anchor) {
   var button=anchor.ButtonBounds;
   int left=anchor.ToolbarBounds.IsEmpty
    ? button.Left-(int)Math.Round(680*anchor.Scale) : anchor.ToolbarBounds.Left;
   int padding=(int)Math.Round(12*anchor.Scale);
   return Rectangle.Intersect(anchor.WindowBounds,Rectangle.FromLTRB(left+padding,
    button.Top-(int)Math.Round(12*anchor.Scale),button.Left-(int)Math.Round(12*anchor.Scale),
    button.Bottom+(int)Math.Round(8*anchor.Scale)));
  }

  internal static Color SelectBackground(IEnumerable<Color> samples,Color previous) {
   var groups=new Dictionary<int,List<Color>>();
   int count=0;
   foreach(var sample in samples) {
    int maximum=Math.Max(sample.R,Math.Max(sample.G,sample.B));
    int minimum=Math.Min(sample.R,Math.Min(sample.G,sample.B));
    if(maximum-minimum>12) continue;
    int key=((sample.R+4)/8)*1024+((sample.G+4)/8)*32+(sample.B+4)/8;
    List<Color> group;
    if(!groups.TryGetValue(key,out group)) { group=new List<Color>(); groups.Add(key,group); }
    group.Add(sample); count++;
   }
   List<Color> dominant=null;
   foreach(var group in groups.Values) if(dominant==null||group.Count>dominant.Count) dominant=group;
   if(dominant==null||dominant.Count<12||dominant.Count<count*0.3) return previous;
   dominant.Sort(delegate(Color first,Color second) { return first.GetBrightness().CompareTo(second.GetBrightness()); });
   var color=dominant[dominant.Count/2];
   return Color.FromArgb(color.R,color.G,color.B);
  }

  internal static Color Sample(Bitmap image,Rectangle bounds,Func<Point,bool> visible,Color previous) {
   var colors=new List<Color>(); AcceptedSamples=0; OccludedSamples=0;
   for(int row=0;row<5;row++) for(int column=0;column<31;column++) {
    int x=bounds.Left+(int)Math.Round((column+0.5)*bounds.Width/31);
    int y=bounds.Top+(int)Math.Round((row+0.5)*bounds.Height/5);
    var point=new Point(x,y);
    if(!visible(point)) { OccludedSamples++; continue; }
    int localX=x-bounds.Left,localY=y-bounds.Top;
    if(localX<0||localY<0||localX>=image.Width||localY>=image.Height) continue;
    colors.Add(image.GetPixel(localX,localY)); AcceptedSamples++;
   }
   return SelectBackground(colors,previous);
  }

  internal static Color ReadBackground(ButtonAnchor anchor,Color previous) {
   try {
    var bounds=Rectangle.Intersect(SamplingBounds(anchor),SystemInformation.VirtualScreen);
    if(bounds.Width<80||bounds.Height<12) return previous;
    using(var image=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb)) {
     using(var graphics=Graphics.FromImage(image))
      graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);
     return Sample(image,bounds,delegate(Point point) {
      var window=NativeAnchor.WindowFromPoint(point);
      return window!=IntPtr.Zero&&NativeAnchor.GetAncestor(window,2)==anchor.Window;
     },previous);
    }
   } catch { return previous; }
  }
 }
}
