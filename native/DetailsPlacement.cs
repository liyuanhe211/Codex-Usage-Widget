using System.Drawing;

namespace UsageRings {
 internal sealed class DetailsPlacement {
  private ButtonAnchor openingAnchor;
  internal bool HasPendingChange { get; private set; }
  internal void Capture(ButtonAnchor anchor) { openingAnchor=anchor; HasPendingChange=false; }
  internal void SelectionChanged() { if(openingAnchor!=null) HasPendingChange=true; }
  internal ButtonAnchor FollowWindow(Rectangle windowBounds) {
   if(openingAnchor==null) return null;
   var button=openingAnchor.ButtonBounds;
   button.Offset(windowBounds.Left-openingAnchor.WindowBounds.Left,windowBounds.Top-openingAnchor.WindowBounds.Top);
   return new ButtonAnchor { Window=openingAnchor.Window,WindowBounds=windowBounds,
    ButtonBounds=button,Scale=openingAnchor.Scale,ButtonName=openingAnchor.ButtonName };
  }
 }
}
