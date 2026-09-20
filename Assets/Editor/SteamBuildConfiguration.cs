using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Justitia.Editor
{
    public sealed class SteamBuildConfiguration : IPreprocessBuildWithReport,IPostprocessBuildWithReport
    {
        public int callbackOrder=>0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if(SteamClientRuntime.AppId==0 || (SteamClientRuntime.AppId==480 && (report.summary.options&BuildOptions.Development)==0))
                throw new BuildFailedException("Set the production Steam App ID in Assets/Resources/SteamConfig.json before a release build. App ID 480 is development only.");
        }
        public void OnPostprocessBuild(BuildReport report)
        {
            var folder=Path.GetDirectoryName(report.summary.outputPath);
            var path=Path.Combine(folder,"steam_appid.txt");
            if((report.summary.options&BuildOptions.Development)!=0)File.WriteAllText(path,SteamClientRuntime.AppId.ToString());
            else if(File.Exists(path))File.Delete(path);
            // Keep native executables outside Assets so Unity does not import backend DLLs as plugins.
            if(report.summary.platform==BuildTarget.StandaloneWindows64)
            {
                var source=Path.GetFullPath("Tools/Whisper");
                if(!File.Exists(Path.Combine(source,"ggml-small-q5_1.bin")))throw new BuildFailedException("Run Tools/setup-stt.ps1 before building STT.");
                var target=Path.Combine(folder,"Tools","Whisper");Directory.CreateDirectory(Path.Combine(target,"Release"));
                foreach(var file in Directory.GetFiles(Path.Combine(source,"Release")))
                    if(Path.GetExtension(file)==".dll" || Path.GetFileName(file)=="whisper-cli.exe")
                        File.Copy(file,Path.Combine(target,"Release",Path.GetFileName(file)),true);
                foreach(var name in new[]{"ggml-small-q5_1.bin","LICENSE-whisper.txt","LICENSE-model.txt"})
                    File.Copy(Path.Combine(source,name),Path.Combine(target,name),true);
            }
        }
    }
}
