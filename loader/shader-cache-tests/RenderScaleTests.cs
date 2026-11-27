using System; using System.Linq; using System.IO; using System.Drawing; using System.Diagnostics;
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
Check(RenderResolution.SportFillRefusal(saved).Contains("Native renderer"),"GT Sport cannot keep its menus 16:9 on the old renderer");
// FIX-RT-049: the saved shape, the real screen's shape, and what each game is handed.
profile.Clear();
bool tallRenderer=false;
bool tallScene=false;
Func<bool,string,string,string,Size,RenderResolution.ScenePlan> plan=(g,preset,mode,pro,output)=>RenderResolution.Plan(g,preset,mode,pro,output,saved,name=>name=="RYGT_TALL_SCREEN"?tallRenderer:name=="RYGT_TALL_SCENE"&&tallScene);
Func<RenderResolution.ScenePlan,string> told=p=>String.Join(" ",new[]{p.Scene.HasValue?p.Scene.Value.Width+"x"+p.Scene.Value.Height:"game",p.Aspect,p.Kind,p.Wide?"wide":"",p.Tall?"tall":"",p.FromShape?"shape":"",p.SportPicture?"picture":"",p.Refusal!=null?"refused":""}.Where(s=>s!=""));
Size wide1080 = new Size(2560,1080), wide1600 = new Size(3840,1600), superwide = new Size(5120,1440), tall = new Size(1920,1200);
Check(RenderResolution.SportFillRefusal(saved)==null,"GT Sport fills with the native renderer by default");
Check(RenderResolution.SavedShape(saved,"game")=="16:9" && !RenderResolution.SavedStretch(saved,"game") && told(plan(false,null,"game","off",wide1440))=="game 16:9" && told(plan(true,null,"game","off",wide1440))=="game 16:9","no saved shape is 16:9 in both games, and Native is the default");
profile["aspect_ratio"]="auto";
Check(RenderResolution.SavedShape(saved,"game")=="auto","a shape saved before 0.3.20 counts with the game's own render resolution");
Check(RenderResolution.SavedShape(saved,"3440x1440")=="16:9" && RenderResolution.SavedShape(saved,RenderResolution.Auto)=="16:9","the older key is the render resolution's own beside another mode");
profile["screen_aspect"]="16:9";
Check(RenderResolution.SavedShape(saved,"game")=="16:9","the new key wins over the older one");
profile["screen_aspect"]="21:9";
Check(RenderResolution.SavedShape(saved,"2560x1440")=="21:9","the new key holds with any render resolution");
foreach (Size real in new[] {wide1080,wide1440,wide1600}) Check(RenderResolution.ExactShape("21:9",real)=="auto","21:9 follows "+real);
Check(RenderResolution.ExactShape("21:9",superwide)=="21:9" && RenderResolution.ExactShape("21:9",fhd)=="21:9","21:9 stays a choice on other screens");
Check(RenderResolution.ExactShape("32:9",superwide)=="auto" && RenderResolution.ExactShape("16:10",tall)=="auto" && RenderResolution.ExactShape("16:10",fhd)=="16:10","32:9 and 16:10");
Check(RenderResolution.ExactShape("16:9",fhd)=="16:9" && RenderResolution.ExactShape("auto",wide1440)=="auto" && RenderResolution.ExactShape("bad",fhd)=="bad","16:9, auto and unknown values are left alone");
// FIX-RT-050: one plan for both games. A wide shape saved without GPU.screen_stretch (before the key
// existed) is the old method, stretched from the 16:9 scene.
Check(RenderResolution.SavedStretch(saved,"game"),"a wide shape saved before the key was stretched");
foreach (bool g in new[] {false,true}) {
    string picture=g?" picture":"";
    Check(told(plan(g,null,"game","off",wide1440))=="game auto stretched"+picture,"an older wide shape stays stretched, gts="+g);
    Check(told(plan(g,null,"game","1080p",wide1440))=="game auto stretched"+picture,"stretched on a Pro 1080p TV, gts="+g);
    Check(told(plan(g,"32:9","game","off",wide1440))=="game 32:9","a shape set outside the page wins, gts="+g);
    Check(told(plan(g,null,RenderResolution.Auto,"off",wide1440))=="3440x1440 3440:1440 wide","Auto in an ultrawide window is its own shape, gts="+g);
    Check(told(plan(g,null,"5120x1440","off",superwide))=="5120x1440 5120:1440 wide","an ultrawide render resolution is its own shape, gts="+g);
}
Check(told(plan(false,null,"game","4k",superwide))=="game 21:9 stretched" && told(plan(true,null,"game","4k",wide1440))=="game 16:9","Pro 4K TV: GT7 keeps its shape, GT Sport stays 16:9");
Check(told(plan(false,null,"2560x1440","off",wide1440))=="2560x1440 auto stretched","GT7 stretches a fixed 16:9 scene");
Check(told(plan(true,null,"2560x1440","off",wide1440))=="2560x1440 auto refused" && plan(true,null,"2560x1440","off",wide1440).Refusal.Contains("Shape rendering to Native"),"GT Sport's stretch beside a fixed scene is refused with the way out");
Check(told(plan(true,null,"game","off",fhd))=="game 16:9" && told(plan(true,null,"game","off",tall))=="game 16:9","GT Sport's wide shape does nothing on a screen that is not wider");
profile["native_renderer"]=false;
Check(plan(true,null,"game","off",wide1440).Refusal.Contains("Native renderer") && plan(false,null,"game","off",wide1440).Refusal==null,"GT Sport's stretch needs the native renderer");
profile.Remove("native_renderer");
// Native: the scene at the screen's shape, at the render resolution's height. The same for both games.
profile["screen_stretch"]=false;
Check(!RenderResolution.SavedStretch(saved,"game"),"the key wins over the older rule");
foreach (bool g in new[] {false,true}) {
    Check(told(plan(g,null,"game","off",wide1440))=="2576x1080 2576:1080 wide shape","native at the game's height, gts="+g);
    Check(told(plan(g,null,"2560x1440","off",wide1440))=="3440x1440 3440:1440 wide shape","native at 1440 lines, gts="+g);
    Check(told(plan(g,null,"3840x2160","off",wide1440))=="4432x1856 4432:1856 wide shape","native within the 4K pixel budget, gts="+g);
    Check(told(plan(g,null,"game","off",wide1080))=="2560x1080 2560:1080 wide shape","native on a 2560 x 1080 screen, gts="+g);
    Check(told(plan(g,"32:9","game","off",wide1440))=="game 32:9","a shape set outside the page is not rendered natively, gts="+g);
    var other=plan(g,null,"game","off",superwide);
    Check(told(other)=="2512x1080 2512:1080 wide shape refused" && other.Refusal.Contains("same aspect ratio") && other.Refusal.Contains("Match the window"),"a named shape that is not the screen's is refused with the way out, gts="+g);
    foreach (string upscaler in new[] {"dlss_enabled","fsr_enabled"}) {
        profile[upscaler]=true;
        Check(plan(g,null,"game","off",wide1440).Refusal.Contains("or set Shape rendering to Stretched."),upscaler+" with a native shape names Stretched, gts="+g);
        Check(plan(g,null,"2560x1440","off",wide1440).Refusal.Contains("Game default and Shape rendering to Stretched"),upscaler+" with a native shape and a fixed height, gts="+g);
        profile.Remove(upscaler);
    }
}
Check(plan(false,null,"game","1080p",wide1440).Refusal.Contains("base PS4") && plan(false,null,"game","1080p",wide1440).Refusal.Contains("Stretched"),"GT7's native wide scene needs base mode, and says the way out");
Check(told(plan(true,null,"game","1080p",wide1440))=="2576x1080 2576:1080 wide shape","GT Sport's native wide scene on a Pro 1080p TV");
Check(told(plan(false,null,"game","4k",wide1440))=="game auto stretched" && told(plan(true,null,"game","4k",wide1440))=="game 16:9","Pro 4K TV with Native: the game sets the scene");
profile["native_renderer"]=false;
Check(plan(true,null,"game","off",wide1440).Refusal.Contains("Native renderer") && plan(true,null,"game","off",wide1440).Refusal.Contains("Screen shape to 16:9"),"GT Sport's native shape needs the native renderer");
profile.Remove("native_renderer");
profile["screen_stretch"]=true;
Check(told(plan(false,null,"game","off",wide1440))=="game auto stretched" && told(plan(true,null,"game","off",wide1440))=="game auto stretched picture","Stretched chosen on the page");
// Taller shapes: from the 16:9 scene in both games, whatever Shape rendering says.
profile["screen_aspect"]="16:10";
foreach (bool stretch in new[] {false,true}) {
    profile["screen_stretch"]=stretch;
    Check(told(plan(false,null,"game","off",fhd))=="game 16:10 taller" && told(plan(false,null,"game","off",tall))=="game auto taller","GT7 takes a taller shape");
    Check(told(plan(true,null,"game","off",wide1440))=="game 16:9" && told(plan(true,null,"game","off",fhd))=="game 16:9","GT Sport's 16:10 does nothing on a screen that is not taller");
    tallRenderer=false;
    Check(told(plan(true,null,"game","off",tall))=="game auto taller-ui","an older renderer stretches GT Sport's interface on a 16:10 screen");
    Check(told(plan(true,null,"game","off",new Size(1280,1024)))=="game 16:10 taller-ui","GT Sport's 16:10 stays 16:10 on a 5:4 screen");
    tallRenderer=true;
    Check(told(plan(true,null,"game","off",tall))=="game auto taller picture","GT Sport's 16:10 keeps menus and HUD 16:9 with FEAT-SGT-203");
    Check(told(plan(true,null,"game","4k",tall))=="game 16:9","GT Sport's 16:10 is off on a Pro 4K TV");
    Check(plan(true,null,"2560x1440","off",tall).Refusal.Contains("taller screen"),"GT Sport's 16:10 beside a fixed scene is refused");
    Check(told(plan(false,null,"2560x1440","off",tall))=="2560x1440 auto taller","GT7's 16:10 beside a fixed scene");
}
profile["native_renderer"]=false;
Check(plan(true,null,"game","off",tall).Refusal.Contains("Native renderer"),"GT Sport's 16:10 picture needs the native renderer");
profile.Remove("native_renderer");
tallRenderer=false;
profile["screen_aspect"]="auto";
Check(told(plan(true,null,"game","off",tall))=="game auto taller-ui" && told(plan(false,null,"game","off",tall))=="game auto taller","Match follows a taller screen");
Check(told(plan(true,null,"game","off",fhd))=="game auto" && told(plan(false,null,"game","off",fhd))=="game auto","Match on a 16:9 screen changes nothing");
// FEAT-RT-051: a taller shape rendered natively where the renderer can (GT Sport with FEAT-SGT-210).
tallRenderer=true; tallScene=true;
profile["screen_aspect"]="16:10"; profile["screen_stretch"]=false;
Size tall43=new Size(1440,1080), tall1600=new Size(2560,1600);
Check(told(plan(true,null,"game","off",tall))=="1920x1200 1920:1200 tall shape","GT Sport's native 16:10 scene");
Check(told(plan(true,null,"2560x1440","off",tall1600))=="2560x1600 2560:1600 tall shape","the render resolution gives the width of a taller scene");
Check(told(plan(true,null,"3840x2160","off",tall))=="3456x2160 3456:2160 tall shape","a taller scene stays within 2160 lines");
Check(told(plan(true,null,"game","off",fhd))=="game 16:9" && told(plan(true,null,"game","off",wide1440))=="game 16:9","a taller shape needs a taller screen in GT Sport");
Check(told(plan(true,null,"game","4k",tall))=="game 16:9","Pro 4K TV keeps the game's scene on a taller screen");
var proTall=plan(true,null,"game","1080p",tall);
Check(proTall.Refusal!=null && proTall.Refusal.Contains("base PS4") && proTall.Refusal.Contains("Stretched"),"GT Sport's native taller scene needs base mode");
profile["dlss_enabled"]=true;
Check(plan(true,null,"game","off",tall).Refusal.Contains("Shape rendering to Stretched"),"upscaling with a native taller scene names Stretched");
profile.Remove("dlss_enabled");
Check(told(plan(false,null,"game","off",tall))=="game auto taller","GT7 has no native taller scene yet");
profile["screen_aspect"]="4:3";
Check(told(plan(true,null,"game","off",tall43))=="1920x1440 1920:1440 tall shape","GT Sport's native 4:3 scene");
var other43=plan(true,null,"game","off",tall);
Check(other43.Refusal!=null && other43.Refusal.Contains("same aspect ratio") && other43.Refusal.Contains("Match the window"),"a named taller shape that is not the screen's is refused");
profile["screen_stretch"]=true;
Check(told(plan(true,null,"game","off",tall43))=="game auto taller picture","Stretched keeps the 16:9 scene on a taller screen");
tallScene=false; profile["screen_stretch"]=false;
Check(told(plan(true,null,"game","off",tall43))=="game auto taller picture","a renderer without the taller scene keeps the 16:9 scene");
tallRenderer=false;
Check(RenderResolution.SupportsTallScene(new Size(1920,1200)) && RenderResolution.SupportsTallScene(new Size(1920,1440)) && !RenderResolution.SupportsTallScene(new Size(1920,1080)) && !RenderResolution.SupportsTallScene(new Size(1080,1920)) && !RenderResolution.SupportsTallScene(new Size(3840,2400)),"taller scene limits");
Check(RenderResolution.IsTallOutput(tall) && !RenderResolution.IsTallOutput(fhd) && !RenderResolution.IsTallOutput(wide1440),"taller screens");
profile.Clear();
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

