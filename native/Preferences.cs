using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace UsageRings {
 internal sealed class WidgetPreferences {
  internal readonly string Path;
  internal bool PlaceBesideModel;
  internal string Error;
  internal WidgetPreferences(string root) {
   Path=System.IO.Path.Combine(root,"Usage_Widget_Settings_Private.json");
   try {
    var deployment=System.IO.Path.Combine(root,"native","Local_Deployment_Private.json");
    if(File.Exists(deployment)) {
     var metadata=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(deployment,Encoding.UTF8));
     var configured=Values.Text(Values.At(metadata,"settingsPath"));
     if(!String.IsNullOrEmpty(configured)&&System.IO.Path.IsPathRooted(configured)) Path=System.IO.Path.GetFullPath(configured);
    }
    if(File.Exists(Path)) {
     var value=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path,Encoding.UTF8));
     PlaceBesideModel=Values.At(value,"placeBesideModel") is bool&&(bool)Values.At(value,"placeBesideModel");
    }
   } catch { Error="Could not load the saved position"; }
  }
  internal void Save(bool besideModel) {
   PlaceBesideModel=besideModel;
   var temporary=Path+".tmp";
   try {
    var content=new JavaScriptSerializer().Serialize(new { version=1,placeBesideModel=besideModel });
    File.WriteAllText(temporary,content+Environment.NewLine,new UTF8Encoding(false));
    if(File.Exists(Path)) File.Replace(temporary,Path,null);
    else File.Move(temporary,Path);
    Error=null;
   } catch { Error="Could not save the position"; }
  }
 }
}
