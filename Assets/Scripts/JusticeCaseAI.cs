using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Justitia
{
    public sealed class JusticeCaseAI : MonoBehaviour
    {
        [Serializable] public sealed class Result {public bool ok;public string case_summary,common_question,error;}
        [Serializable] private sealed class Config {public string pythonExecutable,sourceDirectory;}
        [Serializable] private sealed class Request {public string host_keyword,guest_keyword;}
        private LanLobbyConnection network;
        private Task<Result> task;
        private CancellationTokenSource cancellation;
        private string sessionId;
        public bool Busy=>task!=null;
        public void Initialize(LanLobbyConnection connection){network=connection;network.Changed+=OnNetworkChanged;}
        public void Generate()
        {
            if(Busy || !network.BeginCaseGeneration())return;
            sessionId=network.Session.SessionId;
            string root=Path.Combine(Path.GetDirectoryName(Application.dataPath),"Tools","JusticeAI");
            string json=JsonUtility.ToJson(new Request{host_keyword=network.Session.HostKeyword,guest_keyword=network.Session.GuestKeyword});
            cancellation=new CancellationTokenSource();var token=cancellation.Token;
            task=Task.Run(()=>Run(root,json,token));
        }
        public static Result Run(string root,string input,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var config=JsonUtility.FromJson<Config>(File.ReadAllText(Path.Combine(root,"local.json"),Encoding.UTF8).TrimStart('\uFEFF'));
            if(config==null || !File.Exists(config.pythonExecutable) || !File.Exists(Path.Combine(config.sourceDirectory,"main.py")))
                throw new InvalidOperationException("Host PC의 Python 및 AI 원본 경로를 확인해 주세요.");
            var info=new System.Diagnostics.ProcessStartInfo
            {
                FileName=config.pythonExecutable,Arguments="-X utf8 \""+Path.Combine(root,"bridge.py")+"\"",
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,
                RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
            };
            using(var process=System.Diagnostics.Process.Start(info))
            {
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                process.StandardInput.Write(input);process.StandardInput.Close();
                var timer=System.Diagnostics.Stopwatch.StartNew();
                while(!process.WaitForExit(100))
                {
                    if(token.IsCancellationRequested || timer.Elapsed.TotalSeconds>60)
                    {try{process.Kill();process.WaitForExit(3000);}catch(InvalidOperationException){}token.ThrowIfCancellationRequested();throw new TimeoutException("AI 응답 시간이 초과되었습니다.");}
                }
                token.ThrowIfCancellationRequested();errors.GetAwaiter().GetResult();
                string json=output.GetAwaiter().GetResult();
                if(json.Length>8192)throw new InvalidOperationException("AI 응답이 너무 큽니다.");
                var result=JsonUtility.FromJson<Result>(json);
                if(result==null)throw new InvalidOperationException("AI 응답을 읽지 못했습니다.");
                if(process.ExitCode!=0)result.ok=false;
                return result;
            }
        }
        private void Update()
        {
            if(task==null || !task.IsCompleted)return;
            var finished=task;task=null;cancellation.Dispose();cancellation=null;
            if(finished.IsCanceled)return;
            if(finished.IsFaulted)
            {network.CompleteCaseGeneration(sessionId,"","","Host AI 실행 실패. Tools/JusticeAI/local.json의 경로와 Python 환경을 확인해 주세요.");return;}
            var result=finished.Result;
            network.CompleteCaseGeneration(sessionId,result.case_summary,result.common_question,result.ok?"":(result.error??"AI 사건 생성에 실패했습니다."));
        }
        private void OnNetworkChanged()
        {if(network.Stage==ConnectionStage.Offline || (sessionId!=null && network.Session.SessionId!=sessionId))cancellation?.Cancel();}
        private void OnDestroy(){if(network)network.Changed-=OnNetworkChanged;cancellation?.Cancel();}
    }
}
