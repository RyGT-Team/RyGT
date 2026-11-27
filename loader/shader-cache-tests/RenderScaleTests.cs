using System; using System.IO; using System.Drawing; using System.Diagnostics;
namespace RyGT { class RenderScaleTests {
static void Check(bool value,string message){if(!value)throw new Exception(message);}
static void SizeIs(int w,int h,string p,int sw,int sh){Check(RenderResolution.Calculate(w,h,p)==new Size(sw,sh),p+" size");}
static void Main(){
Check(RenderResolution.NativeSize(RenderResolution.Auto, new Size(2560,1440)) == new Size(2560,1440), "match 1440p");
Check(RenderResolution.NativeSize(RenderResolution.Auto, new Size(1280,800)) == new Size(1280,720), "fit 16:10 output");
foreach (var window in new[] {new Size(2560,1080),new Size(3440,1440),new Size(5120,1440)})
    Check(RenderResolution.NativeSize(RenderResolution.Auto, window) == window, "Auto matches the ultrawide window");
foreach (var window in new[] {new Size(7680,2160),new Size(3443,1440),new Size(10240,2880)}) {
    var fit = RenderResolution.NativeSize(RenderResolution.Auto, window).Value;
    Check(RenderResolution.SupportsWideScene(fit) && RenderResolution.WideOutputWarning(fit,window)==null, "Auto fits renderer limits at the output aspect");
}
foreach (int width in new[] { 3440, 5120 }) {
    var wide = new Size(width, 1440);
    Check(RenderResolution.NativeSize(RenderResolution.UltrawideWindow, wide) == wide, "native ultrawide " + width);
}
Check(!RenderResolution.NativeSize(RenderResolution.UltrawideWindow, new Size(2560,1440)).HasValue, "16:9 output");
Check(!RenderResolution.NativeSize(RenderResolution.UltrawideWindow, new Size(7680,2160)).HasValue, "wide pool limit");
Check(RenderResolution.NativeSize(RenderResolution.Auto, new Size(7680,4320)) == new Size(3840,2160), "UHD guest allocation limit");
foreach (string mode in new[] {"2560x1440","3440x1440","5120x1440","3840x1080","4096x1152","3200x1000"}) {
    var size = RenderResolution.NativeSize(mode,new Size(5120,1440));
    Check(size.HasValue && RenderResolution.ModeKey(size.Value)==mode,"exact preset or custom "+mode);
}
foreach (string mode in new[] {"-1x1080","3441x1440","8192x1440","7680x2160","bad"}) {
    Check(!RenderResolution.NativeSize(mode,new Size(5120,1440)).HasValue,"unsupported dimensions "+mode);
}
var settings = new System.Collections.Generic.Dictionary<string,object> {
    {"native_resolution","game"},{"wide_scene",true},{"aspect_ratio","auto"},{"wide_scene_height","1080"} };
Func<string,object> get = k => settings.ContainsKey(k) ? settings[k] : null;
Check(RenderResolution.SavedMode(true,get,new Size(5120,1440))=="3840x1080","legacy GT Sport native 32:9 at 1080p");
settings["native_resolution"]="display";
Check(RenderResolution.SavedMode(true,get,new Size(5120,1440))=="3840x1080","legacy GT Sport wide height still takes precedence");
Check(RenderResolution.SavedMode(false,get,new Size(5120,1440))==RenderResolution.Auto,"legacy Auto follows the output");
settings["native_resolution"]=RenderResolution.Auto;
Check(RenderResolution.SavedMode(true,get,new Size(5120,1440))==RenderResolution.Auto,"Auto stays dynamic after GT Sport writes its scene height");
settings["native_resolution"]="game";
settings["native_renderer"]=false;
Check(RenderResolution.SavedMode(true,get,new Size(5120,1440))=="game","inactive legacy GT Sport wide profile");
settings["native_resolution"]=RenderResolution.UltrawideWindow;
Check(RenderResolution.SavedMode(false,get,new Size(5120,1440))=="5120x1440","legacy GT7 native 32:9");
Check(RenderResolution.NativeSize("3840x2160", new Size(1280,720)) == new Size(3840,2160), "fixed scene independent of window");
Check(!RenderResolution.NativeSize("game", new Size(1280,720)).HasValue, "keep console settings");
SizeIs(1920,1080,"performance",960,540);SizeIs(1920,1080,"quality",1280,720);SizeIs(1920,1080,"ultra_performance",640,360);SizeIs(1920,1080,"balanced",1136,639);SizeIs(3840,2160,"quality",2560,1440);SizeIs(3840,2160,"dlaa",3840,2160);SizeIs(1280,720,"performance",640,360);
// FIX-RT-048: one refusal decision for the Display page's preview and for Play.
var profile = new System.Collections.Generic.Dictionary<string,object>();
Func<string,object> saved = k => profile.ContainsKey(k) ? profile[k] : null;
Size uhd = new Size(3840,2160), fhd = new Size(1920,1080), wide1440 = new Size(3440,1440);
Check(RenderResolution.Refusal(false,saved,null,fhd,"off")==null,"game default is never refused");
Check(RenderResolution.Refusal(false,saved,uhd,fhd,"off")==null,"fixed 4K scene without upscaling");
Check(RenderResolution.Refusal(false,saved,uhd,fhd,"1080p")==null,"fixed 4K scene on a Pro 1080p TV");
Check(RenderResolution.Refusal(false,saved,wide1440,wide1440,"off")==null,"GT7 native ultrawide in base mode");
Check(RenderResolution.Refusal(false,saved,wide1440,wide1440,"1080p").Contains("base PS4"),"GT7 native ultrawide needs base mode");
Check(RenderResolution.Refusal(true,saved,wide1440,wide1440,"1080p")==null,"GT Sport native ultrawide on a Pro 1080p TV");
Check(RenderResolution.Refusal(true,saved,wide1440,fhd,"off").Contains("same aspect ratio"),"wide scene in a 16:9 window");
foreach (string upscaler in new[] {"dlss_enabled","fsr_enabled"}) {
    profile[upscaler]=true;
    foreach (bool gtsGame in new[] {false,true}) {
        Check(RenderResolution.Refusal(gtsGame,saved,uhd,fhd,"1080p").Contains("upscaling"),upscaler+" with a fixed scene is refused");
        Check(RenderResolution.Refusal(gtsGame,saved,wide1440,wide1440,"off").Contains("upscaling"),upscaler+" with a wide scene is refused");
        // Auto in a 1920 x 1080 window: the game's own scene, nothing to patch.
        Check(RenderResolution.Refusal(gtsGame,saved,RenderResolution.NativeSize(RenderResolution.Auto,fhd),fhd,"off")==null,upscaler+" with Auto at the game's own size");
        Check(RenderResolution.Refusal(gtsGame,saved,fhd,fhd,"1080p").Contains("upscaling"),upscaler+": the Pro 1080p scene is patched even at 1920 x 1080");
        Check(RenderResolution.Refusal(gtsGame,saved,uhd,fhd,"4k")==null,upscaler+": the game sets the scene on a Pro 4K TV");
    }
    profile.Remove(upscaler);
}
profile["native_renderer"]=false;
Check(RenderResolution.Refusal(true,saved,uhd,fhd,"off").Contains("Native renderer"),"old renderer with a fixed scene");
profile.Remove("native_renderer"); profile["experimental_render_scale"]=true;
Check(RenderResolution.Refusal(true,saved,uhd,fhd,"off").Contains("Native renderer"),"experimental scale with a fixed scene");
Check(RenderResolution.IsGameDefault(fhd,"off") && !RenderResolution.IsGameDefault(fhd,"1080p") && !RenderResolution.IsGameDefault(uhd,"off") && RenderResolution.IsGameDefault(null,"1080p"),"game default sizes");
string dir=Path.Combine(Path.GetTempPath(),"RyGT-render-scale-"+Guid.NewGuid());Directory.CreateDirectory(dir);
try { byte[] pe=new byte[512];Action<int,int> put=(at,n)=>Array.Copy(BitConverter.GetBytes(n),0,pe,at,4);put(0x3c,0x80);pe[0x86]=3;int table=0x98;int[] rvas={0x1988ad3,0x1988ab0,0x1988ab9};string[] signatures={"48b98007000038040000","8b0d72ea2301","8b0d6dea2301"};for(int i=0;i<3;i++){int at=table+i*40,raw=400+i*32;put(at+12,rvas[i]);put(at+16,signatures[i].Length/2);put(at+20,raw);for(int j=0;j<signatures[i].Length/2;j++)pe[raw+j]=Convert.ToByte(signatures[i].Substring(j*2,2),16);}File.WriteAllBytes(Path.Combine(dir,"GTSport.exe"),pe);
var start=new ProcessStartInfo();start.EnvironmentVariables.Remove("SHADGT_PATCH");RenderResolution.ConfigureStart(start,dir,new Size(960,540));Check(start.EnvironmentVariables["SHADGT_PATCH"].Contains("48b9c00300001c020000"),"allocation patch");Check(start.EnvironmentVariables["SHADGT_SCENE_WIDTH"]=="960","scene env");Check(start.EnvironmentVariables["SHADGT_EXPERIMENTAL_SCENE_VIEWPORT"]=="1","viewport hook");
var off=new ProcessStartInfo();off.EnvironmentVariables["SHADGT_SCENE_WIDTH"]="960";RenderResolution.ConfigureStart(off,dir,null);Check(off.EnvironmentVariables["SHADGT_SCENE_WIDTH"]==null,"disabled clears inherited scene size");
var conflict=new ProcessStartInfo();conflict.EnvironmentVariables["SHADGT_PATCH"]="custom";bool rejected=false;try{RenderResolution.ConfigureStart(conflict,dir,new Size(960,540));}catch(InvalidOperationException){rejected=true;}Check(rejected,"patch conflict");
pe[400]^=1;File.WriteAllBytes(Path.Combine(dir,"GTSport.exe"),pe);var invalid=new ProcessStartInfo();invalid.EnvironmentVariables.Remove("SHADGT_PATCH");rejected=false;try{RenderResolution.ConfigureStart(invalid,dir,new Size(960,540));}catch(InvalidOperationException){rejected=true;}Check(rejected&&invalid.EnvironmentVariables["SHADGT_PATCH"]==null,"bad signature fail closed");
Console.WriteLine("PASS: presets at 720p/1080p/4K, startup patch bytes, dimensions, disabled cleanup, conflicting patch and signature rejection");
}finally{Directory.Delete(dir,true);}
} } }

