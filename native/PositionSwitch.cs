using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UsageRings {
 internal sealed class PositionSwitch : Control {
  private bool checkedValue;
  internal double DisplayScale=1;
  internal event EventHandler CheckedChanged;
  internal bool Checked {
   get { return checkedValue; }
   set { if(value==checkedValue) return; checkedValue=value; Invalidate();
    if(CheckedChanged!=null) CheckedChanged(this,EventArgs.Empty); }
  }
  internal PositionSwitch() {
   SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer
    |ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
   TabStop=true; Cursor=Cursors.Hand; AccessibleRole=AccessibleRole.CheckButton;
   AccessibleName="Place usage rings beside the model";
   AccessibleDescription="Left: beside model. Right: cover microphone.";
  }
  protected override void OnMouseClick(MouseEventArgs args) {
   base.OnMouseClick(args); if(args.Button==MouseButtons.Left) { Focus(); Checked=!Checked; }
  }
  protected override void OnKeyDown(KeyEventArgs args) {
   base.OnKeyDown(args);
   if(args.KeyCode==Keys.Space||args.KeyCode==Keys.Enter) { Checked=!Checked; args.Handled=true; }
  }
  internal static void PaintSwitch(Graphics graphics,RectangleF bounds,bool selected,bool dark) {
   var foreground=dark?Color.FromArgb(220,220,220):Color.FromArgb(45,45,45);
   var muted=dark?Color.FromArgb(145,145,145):Color.FromArgb(115,115,115);
   using(var font=new Font("Segoe UI",11,FontStyle.Regular,GraphicsUnit.Pixel))
   using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
   using(var label=new SolidBrush(muted))
   using(var first=new SolidBrush(foreground))
   using(var second=new SolidBrush(foreground)) {
    format.FormatFlags|=StringFormatFlags.MeasureTrailingSpaces;
    float modelWidth=graphics.MeasureString("Model",font,PointF.Empty,format).Width;
    float microphoneWidth=graphics.MeasureString("Mic",font,PointF.Empty,format).Width;
    float prefixWidth=graphics.MeasureString("Position: ",font,PointF.Empty,format).Width;
    const float gap=5;
    var track=new RectangleF(bounds.Right-microphoneWidth-gap-34,bounds.Top+4,34,16);
    float modelLeft=track.Left-gap-modelWidth;
    graphics.DrawString("Position: ",font,label,new PointF(modelLeft-prefixWidth,bounds.Top+4),format);
    graphics.DrawString("Model",font,first,new PointF(modelLeft,bounds.Top+4),format);
    graphics.DrawString("Mic",font,second,new PointF(track.Right+gap,bounds.Top+4),format);
    using(var path=new GraphicsPath()) {
     path.AddArc(track.Left,track.Top,16,16,90,180);
     path.AddArc(track.Right-16,track.Top,16,16,270,180); path.CloseFigure();
     using(var brush=new SolidBrush(Color.FromArgb(71,151,237))) graphics.FillPath(brush,path);
    }
    using(var brush=new SolidBrush(Color.White))
     graphics.FillEllipse(brush,selected?track.Left+2:track.Right-14,track.Top+2,12,12);
   }
  }
  protected override void OnPaint(PaintEventArgs args) {
   base.OnPaint(args); args.Graphics.Clear(BackColor); args.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   args.Graphics.ScaleTransform((float)DisplayScale,(float)DisplayScale);
   PaintSwitch(args.Graphics,new RectangleF(0,0,146,24),Checked,BackColor.GetBrightness()<0.5);
  }
 }
}
