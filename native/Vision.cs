using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UsageRings {
 internal static class NativeVision {
  private static readonly string[] Mask = {
   "........................", "........................", "...........##...........",
   ".........######.........", "........###..###........", ".......###....###.......",
   ".......##......##.......", ".......##......##.......", ".......##......##.......",
   ".......##......##.......", ".......##......##.......", ".......##......##.......",
   ".......##......##.......", ".......##.....###.......", "....#...###..###...#....",
   "....##...######...##....", ".....##....##....##.....", ".....###........###.....",
   "......####....####......", "........########........", "...........##...........",
   "...........##...........", "...........##...........", "...........##...........",
   "........................", "........................", "........................"
  };
  [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
  internal static double LastScore;
  internal static int MinimumGray=255, MaximumGray=0;
  internal static ButtonAnchor FindButton(IntPtr window, ButtonAnchor composer) {
   if(composer==null||composer.Window!=window||composer.ModelBounds.IsEmpty) return null;
   NativeAnchor.NativeRectangle nativeBounds;
   if (!NativeAnchor.GetWindowRect(window, out nativeBounds) || NativeAnchor.IsIconic(window)) return null;
   var bounds=nativeBounds.ToRectangle();
   if(bounds!=composer.WindowBounds) return null;
   if(bounds.Width<200 || bounds.Height<200) return null;
   using(var bitmap=new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb)) {
    using(var graphics=Graphics.FromImage(bitmap)) {
     var context=graphics.GetHdc();
     try { if(!PrintWindow(window,context,2)) return null; }
     finally { graphics.ReleaseHdc(context); }
    }
    uint dpi=96;
    try { dpi=NativeAnchor.GetDpiForWindow(window); } catch { }
    var scale=Math.Max(1,dpi/96.0);
    var search=composer.ButtonBounds;
    search.Offset(-bounds.Left,-bounds.Top);
    var match=LocateComposer(bitmap,search,scale);
    if(match.IsEmpty && NativeAnchor.GetForegroundWindow()==window) {
     using(var graphics=Graphics.FromImage(bitmap)) {
      int sourceX=Math.Max(0,bounds.Left),sourceY=Math.Max(0,bounds.Top);
      graphics.CopyFromScreen(sourceX,sourceY,sourceX-bounds.Left,sourceY-bounds.Top,
       new Size(bounds.Right-sourceX,bounds.Bottom-sourceY),CopyPixelOperation.SourceCopy);
     }
     match=LocateComposer(bitmap,search,scale);
    }
    if(match.IsEmpty) return null;
    match.Offset(bounds.Left,bounds.Top);
    return new ButtonAnchor { Window=window, WindowBounds=bounds, ButtonBounds=match,
     ButtonName="microphone (verified icon)", Scale=scale,
     ModelBounds=composer.ModelBounds, ToolbarBounds=composer.ToolbarBounds };
   }
  }

  internal static Rectangle LocateComposer(Bitmap image, Rectangle search, double scale) {
   search=Rectangle.Intersect(search,new Rectangle(Point.Empty,image.Size));
   if(search.Width<12||search.Height<16) return Rectangle.Empty;
   using(var cropped=image.Clone(search,PixelFormat.Format32bppArgb)) {
    var match=Locate(cropped,0,scale);
    if(!match.IsEmpty) match.Offset(search.Left,search.Top);
    return match;
   }
  }

  internal static Rectangle Locate(Bitmap image, int startY, double displayScale) {
   var bounds=new Rectangle(0,0,image.Width,image.Height);
   var locked=image.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   var bytes=new byte[Math.Abs(locked.Stride)*image.Height];
   try { Marshal.Copy(locked.Scan0, bytes, 0, bytes.Length); }
   finally { image.UnlockBits(locked); }
   var gray=new byte[image.Width*image.Height];
   for(int y=0;y<image.Height;y++) for(int x=0;x<image.Width;x++) {
    int position=y*Math.Abs(locked.Stride)+x*4;
    gray[y*image.Width+x]=(byte)((bytes[position]+bytes[position+1]+bytes[position+2])/3);
   }
   MinimumGray=255; MaximumGray=0;
   for(int position=startY*image.Width;position<gray.Length;position++) { MinimumGray=Math.Min(MinimumGray,gray[position]); MaximumGray=Math.Max(MaximumGray,gray[position]); }
   var best=Rectangle.Empty;
   double bestScore=0.87;
   foreach(double factor in new[]{0.6,0.667,0.75,0.8,0.9,1.0,1.1,1.25,1.5}) {
    double scale=displayScale*factor;
    int width=(int)Math.Round(24*scale), height=(int)Math.Round(27*scale);
    if(width<12||height<16) continue;
    var on=new List<Point>(); var off=new List<Point>();
    for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
     int templateX=Math.Min(23,(int)(x/scale));
     int templateY=Math.Min(26,(int)(y/scale));
     if(Mask[templateY][templateX]=='#') on.Add(new Point(x,y));
     else {
      bool near=false;
      for(int offsetY=-1;offsetY<=1;offsetY++) for(int offsetX=-1;offsetX<=1;offsetX++) {
       int neighborX=templateX+offsetX, neighborY=templateY+offsetY;
       if(neighborX>=0&&neighborX<24&&neighborY>=0&&neighborY<27&&Mask[neighborY][neighborX]=='#') near=true;
      }
      if(!near) off.Add(new Point(x,y));
     }
    }
    int topX=(int)Math.Round(11*scale), topY=(int)Math.Round(2*scale);
    int stemX=(int)Math.Round(11*scale), stemY=(int)Math.Round(22*scale);
    for(int y=startY;y<image.Height-height;y++) for(int x=0;x<image.Width-width;x++) {
     int origin=y*image.Width+x;
     int background=gray[origin];
     int foreground=gray[origin+topY*image.Width+topX];
     int contrast=foreground-background;
     if(Math.Abs(contrast)<45) continue;
     if(Math.Abs(gray[origin+stemY*image.Width+stemX]-background)<Math.Abs(contrast)*0.25) continue;
     if(Math.Abs(gray[origin+width-1]-background)>25
       ||Math.Abs(gray[origin+(height-1)*image.Width]-background)>25) continue;
     int threshold=background+contrast/3, onHits=0, offHits=0;
     foreach(var point in on) {
      bool hit=false;
      for(int offsetY=-1;offsetY<=1&&!hit;offsetY++) for(int offsetX=-1;offsetX<=1&&!hit;offsetX++) {
       int pixelX=x+point.X+offsetX, pixelY=y+point.Y+offsetY;
       if(pixelX<0||pixelX>=image.Width||pixelY<0||pixelY>=image.Height) continue;
       int value=gray[pixelY*image.Width+pixelX];
       if(contrast>0?value>=threshold:value<=threshold) hit=true;
      }
      if(hit) onHits++;
     }
     double onScore=(double)onHits/on.Count;
     if(onScore<0.94) continue;
     foreach(var point in off) {
      int value=gray[origin+point.Y*image.Width+point.X];
      if(contrast>0?value<threshold:value>threshold) offHits++;
     }
     double offScore=(double)offHits/off.Count;
     if(offScore<0.97) continue;
     double score=onScore*0.7+offScore*0.3;
     if(score>bestScore+0.025 || (score>=bestScore-0.025&&(best.IsEmpty||y>best.Top))) {
      bestScore=score;
      int size=(int)Math.Round(32*scale);
      best=new Rectangle(x+width/2-size/2,y+height/2-size/2,size,size);
     }
    }
   }
   LastScore=bestScore;
   return best;
  }
 }
}
